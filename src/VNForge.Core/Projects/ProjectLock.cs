namespace VNForge.Core.Projects;

/// <summary>Outra operação do VNForge (o app ou outro comando) está escrevendo no jogo deste projeto.</summary>
public sealed class ProjectBusyException()
    : VNForgeException("Outra operação do VNForge está mexendo no jogo deste projeto (o app ou outro comando). Espere ela terminar e tente de novo.");

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
    /// <exception cref="ProjectBusyException">Já há outra operação com a trava.</exception>
    public static ProjectLock Acquire(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Directory.CreateDirectory(project.CacheDirectory);
        try
        {
            return new ProjectLock(new FileStream(Path.Combine(project.CacheDirectory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            throw new ProjectBusyException();
        }
    }

    public void Dispose() => _stream.Dispose();
}
