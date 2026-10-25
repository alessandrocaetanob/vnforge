namespace VNForge.Core.Projects;

/// <summary>Outra operação do VNForge (o app ou outro comando) está escrevendo no jogo deste projeto.</summary>
public sealed class ProjectBusyException()
    : VNForgeException("Outra operação do VNForge está mexendo no jogo deste projeto (o app ou outro comando). Espere ela terminar e tente de novo.");

/// <summary>Não deu para preparar a trava do projeto por um motivo que não é "ocupado": pasta sem permissão, disco com problema.</summary>
public sealed class ProjectLockUnavailableException(Exception inner)
    : VNForgeException("Não foi possível preparar a pasta do projeto para mexer no jogo.", inner);

/// <summary>
/// Trava exclusiva por projeto em <c>.cache/game.lock</c> (arquivo aberto com <see cref="FileShare.None"/>, que o sistema solta se o processo morre).
/// Quem escreve na pasta do jogo (teste no jogo, <c>vnf inject</c>, <c>vnf restore</c>, patch do executável, e o Injetar e o Restaurar do app) a segura
/// do plano até o fim da gravação. Sem ela, dois processos escolheriam a mesma pasta de backup no mesmo minuto, e a falha de um apagaria o backup do outro.
/// </summary>
public sealed class ProjectLock : IDisposable
{
    private const string FileName = "game.lock";

    private readonly FileStream _stream;

    private ProjectLock(FileStream stream) => _stream = stream;

    /// <summary>Pega a trava do projeto, sem esperar.</summary>
    /// <exception cref="ProjectBusyException">Já há outra operação com a trava (violação de compartilhamento do arquivo).</exception>
    /// <exception cref="ProjectLockUnavailableException">Qualquer outra falha de disco ou de permissão ao preparar a trava.</exception>
    public static ProjectLock Acquire(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        try
        {
            Directory.CreateDirectory(project.CacheDirectory);
            return new ProjectLock(new FileStream(Path.Combine(project.CacheDirectory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            throw new ProjectBusyException();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectLockUnavailableException(ex);
        }
    }

    /// <summary>
    /// No Windows o erro é o código 32 (ERROR_SHARING_VIOLATION) ou 33 (ERROR_LOCK_VIOLATION). Nos outros sistemas o .NET traduz o <c>flock</c> ocupado
    /// para uma <see cref="IOException"/> simples, sem esses códigos, então ali vale qualquer <see cref="IOException"/> ao abrir o arquivo.
    /// </summary>
    private static bool IsSharingViolation(IOException ex) =>
        (ex.HResult & 0xFFFF) is 32 or 33
        || (!OperatingSystem.IsWindows() && ex is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException) && ex.GetType() == typeof(IOException));

    public void Dispose() => _stream.Dispose();
}
