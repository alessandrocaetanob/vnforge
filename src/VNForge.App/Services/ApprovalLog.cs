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
/// de uma versão mais nova nunca é sobrescrito: o registro continua na memória, sem gravar, até o arquivo voltar a abrir.
/// </summary>
public sealed class ProjectApprovalLog(string projectRoot) : IApprovalLog
{
    private readonly Lock gate = new();
    private readonly List<ApprovalRecord> unsaved = [];
    private SessionsFile sessions = SessionsFile.Empty;
    private SessionsLoadStatus status = SessionsLoadStatus.Missing;
    private ProjectFolder? folder;
    private bool loaded;
    private SessionsFile? pending;
    private bool writing;
    private Task saving = Task.CompletedTask;

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

    public void Record(string scriptKey, string unitId, DateTimeOffset at)
    {
        lock (gate)
        {
            Load();
            var writable = CanWrite();
            var record = new ApprovalRecord(scriptKey, unitId, at);
            sessions = sessions.With(record);
            if (!writable)
            {
                unsaved.Add(record);
            }

            Persist(writable);
        }
    }

    public void Remove(string scriptKey, string unitId)
    {
        lock (gate)
        {
            Load();
            var writable = CanWrite();
            sessions = sessions.Without(scriptKey, unitId);
            var index = unsaved.FindLastIndex(a => a.ScriptKey == scriptKey && a.UnitId == unitId);
            if (index >= 0)
            {
                unsaved.RemoveAt(index);
            }

            Persist(writable);
        }
    }

    public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone)
    {
        lock (gate)
        {
            Load();
            return sessions.ReviewedOn(now, zone);
        }
    }

    private void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        try
        {
            folder = ProjectFolder.Open(projectRoot);
        }
        catch (VNForgeException)
        {
            // Sem pasta de projeto não há onde gravar: o registro vale só até o app fechar.
            status = SessionsLoadStatus.Unreadable;
            return;
        }

        var read = SessionsFile.Read(folder);
        sessions = read.File;
        status = read.Status;
    }

    private bool CanWrite()
    {
        if (folder is not null && status == SessionsLoadStatus.Unreadable)
        {
            // O arquivo pode ter voltado a abrir: junta o histórico dele com o que já foi aprovado desde então.
            var again = SessionsFile.Read(folder);
            if (again.Status != SessionsLoadStatus.Unreadable)
            {
                status = again.Status;
                sessions = unsaved.Aggregate(again.File, (file, approval) => file.With(approval));
                if (again.CanOverwrite)
                {
                    unsaved.Clear();
                }
            }
        }

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
        while (true)
        {
            SessionsFile next;
            lock (gate)
            {
                if (pending is null)
                {
                    writing = false;
                    return;
                }

                next = pending;
                pending = null;
            }

            try
            {
                SessionsFile.Save(folder!, next);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // O registro é só do contador; a aprovação já está no texto do projeto, e a próxima aprovação grava de novo.
            }
        }
    }
}
