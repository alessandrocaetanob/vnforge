using System.Collections.Concurrent;
using System.Diagnostics;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.Services;

/// <summary>O registro de aprovações do projeto aberto (<c>.cache/sessions.json</c>), atrás de uma interface para o Editor rodar nos testes sem disco.</summary>
public interface IApprovalLog
{
    /// <summary>Guarda que a linha foi aprovada nesta hora.</summary>
    void Record(string scriptKey, string unitId, DateTimeOffset at);

    /// <summary>Tira do registro a última aprovação da linha, quando a pessoa a desfaz.</summary>
    void Remove(string scriptKey, string unitId);

    /// <summary>Quantas linhas diferentes foram aprovadas no dia de <paramref name="now"/>, pelo fuso da pessoa.</summary>
    int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone);
}

/// <summary>
/// O registro do disco. O arquivo é lido uma vez, na primeira vez que o registro é usado, e a memória passa a ser a fonte: aprovar acrescenta na memória, o
/// contador do dia sai dela, e a gravação roda fora da thread da tela, uma por vez, a última vencendo. Um arquivo que não abriu (disco ocupado) ou que é
/// de uma versão mais nova nunca é sobrescrito: o registro continua na memória, sem gravar. Com o arquivo ilegível, uma releitura é tentada a cada
/// <see cref="RetryInterval"/>; ao dar certo, o que foi aprovado e desfeito nesse meio tempo é reaplicado sobre o histórico lido.
/// </summary>
public sealed class ProjectApprovalLog : IApprovalLog
{
    /// <summary>De quanto em quanto tempo um arquivo ilegível é relido; cada tentativa pode esperar até 300 ms na thread da tela.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<string, ProjectApprovalLog> Shared =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string projectRoot;
    private readonly TimeProvider clock;
    private readonly Action<ProjectFolder, SessionsFile> save;
    private readonly Lock gate = new();
    private readonly List<(bool Added, ApprovalRecord Record)> journal = [];
    private SessionsFile sessions = SessionsFile.Empty;
    private SessionsLoadStatus status = SessionsLoadStatus.Missing;
    private ProjectFolder? folder;
    private bool loaded;
    private DateTimeOffset nextRetry;
    private SessionsFile? pending;
    private bool writing;
    private Task saving = Task.CompletedTask;

    /// <param name="projectRoot">A pasta do projeto.</param>
    /// <param name="clock">O relógio da espera entre releituras; o padrão é o do sistema.</param>
    /// <param name="save">Grava o registro; os testes trocam por um que falha ou espera. O padrão é <see cref="SessionsFile.Save"/>.</param>
    public ProjectApprovalLog(string projectRoot, TimeProvider? clock = null, Action<ProjectFolder, SessionsFile>? save = null)
    {
        this.projectRoot = projectRoot;
        this.clock = clock ?? TimeProvider.System;
        this.save = save ?? SessionsFile.Save;
    }

    /// <summary>
    /// O registro compartilhado da pasta do projeto: trocar de projeto e voltar não cria um registro novo que leria o arquivo antes de a gravação pendente do
    /// anterior terminar e a deixaria sobrescrever as aprovações novas.
    /// </summary>
    public static ProjectApprovalLog For(string projectRoot) => For(projectRoot, root => new ProjectApprovalLog(root));

    /// <summary>O registro compartilhado da pasta; <paramref name="create"/> só roda na primeira vez que a pasta aparece.</summary>
    public static ProjectApprovalLog For(string projectRoot, Func<string, ProjectApprovalLog> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(create);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        return Shared.GetOrAdd(full, create);
    }

    /// <summary>Espera as gravações pendentes de todos os registros, até <paramref name="timeout"/>; o app chama ao fechar. Um registro que ficou com algo a gravar sem gravação ativa (a anterior falhou) tenta de novo uma vez. Falso quando alguma não terminou.</summary>
    public static bool FlushAll(TimeSpan timeout)
    {
        var tasks = Shared.Values.Select(log => log.RetryPending()).Where(task => !task.IsCompleted).ToArray();
        return tasks.Length == 0 || Task.WaitAll(tasks, timeout);
    }

    /// <summary>A gravação em andamento, ou uma tarefa já concluída quando não há nenhuma; os testes a aguardam.</summary>
    public Task Saving
    {
        get
        {
            lock (gate)
            {
                return saving;
            }
        }
    }

    /// <summary>Retoma a gravação do que ficou em <c>pending</c> sem tarefa ativa, e devolve a gravação em andamento (ou concluída).</summary>
    private Task RetryPending()
    {
        lock (gate)
        {
            if (!writing && pending is not null)
            {
                writing = true;
                saving = Task.Run(WriteLoop);
            }

            return saving;
        }
    }

    public void Record(string scriptKey, string unitId, DateTimeOffset at)
    {
        lock (gate)
        {
            var writable = CanWrite();
            var record = new ApprovalRecord(scriptKey, unitId, at);
            sessions = sessions.With(record);
            if (!writable)
            {
                journal.Add((true, record));
            }

            Persist(writable);
        }
    }

    public void Remove(string scriptKey, string unitId)
    {
        lock (gate)
        {
            var writable = CanWrite();
            sessions = sessions.Without(scriptKey, unitId);
            if (!writable)
            {
                journal.Add((false, new ApprovalRecord(scriptKey, unitId, default)));
            }

            Persist(writable);
        }
    }

    public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone)
    {
        lock (gate)
        {
            Load();
            RetryUnreadable();
            return sessions.ReviewedOn(now, zone);
        }
    }

    /// <summary>Abre a pasta e lê o arquivo na primeira vez. Sem pasta de projeto ainda não há o que ler, e a próxima chamada tenta de novo.</summary>
    private void Load()
    {
        if (loaded)
        {
            return;
        }

        try
        {
            folder = ProjectFolder.Open(projectRoot);
        }
        catch (VNForgeException)
        {
            status = SessionsLoadStatus.Unreadable;
            return;
        }

        loaded = true;
        Merge(SessionsFile.Read(folder));
    }

    /// <summary>Junta o histórico lido com o que foi aprovado ou desfeito enquanto ele não estava legível. Arquivo ainda ilegível agenda a próxima tentativa.</summary>
    private void Merge(SessionsLoad read)
    {
        status = read.Status;
        sessions = journal.Aggregate(read.File, (file, step) => step.Added ? file.With(step.Record) : file.Without(step.Record.ScriptKey, step.Record.UnitId));
        if (read.CanOverwrite)
        {
            journal.Clear();
        }

        nextRetry = status == SessionsLoadStatus.Unreadable ? clock.GetUtcNow() + RetryInterval : default;
    }

    /// <summary>
    /// Relê o arquivo que estava ilegível quando já passou o <see cref="RetryInterval"/> desde a última tentativa. Se havia aprovações só na memória e a leitura
    /// deu certo, grava o histórico mesclado: quem chama pode ser a contagem, que não grava nada depois.
    /// </summary>
    private void RetryUnreadable()
    {
        if (folder is null || !loaded || status != SessionsLoadStatus.Unreadable || clock.GetUtcNow() < nextRetry)
        {
            return;
        }

        var hadJournal = journal.Count > 0;
        Merge(SessionsFile.Read(folder));
        if (hadJournal)
        {
            Persist(new SessionsLoad(sessions, status).CanOverwrite);
        }
    }

    private bool CanWrite()
    {
        Load();
        RetryUnreadable();
        return folder is not null && new SessionsLoad(sessions, status).CanOverwrite;
    }

    private void Persist(bool writable)
    {
        if (!writable)
        {
            return;
        }

        pending = sessions;
        if (writing)
        {
            return;
        }

        writing = true;
        saving = Task.Run(WriteLoop);
    }

    private void WriteLoop()
    {
        var drained = false;
        try
        {
            while (TakePending() is { } next)
            {
                if (!TrySave(next))
                {
                    return;
                }
            }

            drained = true;
        }
        finally
        {
            if (!drained)
            {
                lock (gate)
                {
                    writing = false;
                }
            }
        }
    }

    /// <summary>O próximo registro a gravar. Quando não há mais nenhum, a gravação termina na mesma passada do lock, para uma aprovação nova não ficar sem quem a grave.</summary>
    private SessionsFile? TakePending()
    {
        lock (gate)
        {
            var next = pending;
            pending = null;
            if (next is null)
            {
                writing = false;
            }

            return next;
        }
    }

    /// <summary>Grava o registro. Falha de disco só perde esta gravação (a próxima aprovação grava de novo); qualquer outra falha também devolve o registro à fila e para o laço.</summary>
    private bool TrySave(SessionsFile next)
    {
        try
        {
            save(folder!, next);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O registro é só do contador; a aprovação já está no texto do projeto.
            Debug.WriteLine(ex);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            lock (gate)
            {
                pending ??= next;
            }

            return false;
        }
    }
}
