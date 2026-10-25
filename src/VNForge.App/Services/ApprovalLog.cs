using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.Services;

/// <summary>O registro de aprovações do projeto aberto (<c>.cache/sessions.json</c>), atrás de uma interface para o Editor rodar nos testes sem disco.</summary>
public interface IApprovalLog
{
    /// <summary>Guarda que a linha foi aprovada nesta hora.</summary>
    void Record(string scriptKey, string unitId, DateTimeOffset at);

    /// <summary>Quantas linhas diferentes foram aprovadas no dia de <paramref name="now"/>, pelo fuso da pessoa.</summary>
    int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone);
}

/// <summary>
/// O registro do disco. Lê o arquivo uma vez e guarda o resultado na memória: cada aprovação acrescenta e regrava, e o contador do dia sai da memória
/// sem reler o arquivo. Falha de disco não derruba a aprovação; o contador continua certo até o app fechar.
/// </summary>
public sealed class ProjectApprovalLog(string projectRoot) : IApprovalLog
{
    private readonly Lock gate = new();
    private SessionsFile? sessions;
    private ProjectFolder? folder;

    public void Record(string scriptKey, string unitId, DateTimeOffset at)
    {
        lock (gate)
        {
            var current = Current();
            sessions = current.With(new ApprovalRecord(scriptKey, unitId, at));
            if (folder is not null)
            {
                try
                {
                    SessionsFile.Save(folder, sessions);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // O registro é só do contador; a aprovação já está no texto do projeto.
                }
            }
        }
    }

    public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone)
    {
        lock (gate)
        {
            return Current().ReviewedOn(now, zone);
        }
    }

    private SessionsFile Current()
    {
        if (sessions is not null)
        {
            return sessions;
        }

        try
        {
            folder = ProjectFolder.Open(projectRoot);
            sessions = SessionsFile.Load(folder);
        }
        catch (VNForgeException)
        {
            sessions = SessionsFile.Empty;
        }

        return sessions;
    }
}
