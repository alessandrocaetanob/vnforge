using System.Diagnostics;

namespace VNForge.Core.Projects;

/// <summary>Dispara o processo do jogo. Fica atrás de uma interface para os testes não abrirem nada de verdade.</summary>
public interface IGameProcessStarter
{
    /// <param name="executablePath">Caminho completo do executável.</param>
    /// <param name="workingDirectory">Pasta do jogo, usada como diretório de trabalho.</param>
    void Start(string executablePath, string workingDirectory);
}

/// <summary>Abre o jogo com o shell do sistema, sem esperar o processo terminar.</summary>
public sealed class ShellGameProcessStarter : IGameProcessStarter
{
    public void Start(string executablePath, string workingDirectory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new VNForgeException($"Não foi possível abrir o jogo: {executablePath}\n{ex.Message}");
        }
    }
}

/// <summary>O executável registrado em <c>game.exe</c> não existe; a mensagem mostra o caminho procurado.</summary>
public sealed class GameExecutableNotFoundException : VNForgeException
{
    public GameExecutableNotFoundException(string executablePath)
        : base($"O executável do jogo não foi encontrado: {executablePath}\nConfira game.path e game.exe no vnforge.json.")
    {
        ExecutablePath = executablePath;
    }

    public string ExecutablePath { get; }
}

/// <summary>Resolve e abre o executável do jogo registrado no <c>vnforge.json</c>.</summary>
public sealed class GameLauncher(IGameProcessStarter starter)
{
    private readonly IGameProcessStarter _starter = starter ?? throw new ArgumentNullException(nameof(starter));

    /// <summary>Caminho completo de <c>game.exe</c>; <c>game.path</c> é relativo à pasta do projeto quando não é absoluto.</summary>
    public static string ResolveExecutable(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var game = project.LoadConfig().Game;
        return Path.GetFullPath(game.Exe, Path.GetFullPath(game.Path, project.Root));
    }

    /// <exception cref="GameExecutableNotFoundException">O executável não existe.</exception>
    /// <returns>O caminho do executável aberto.</returns>
    public string Launch(ProjectFolder project)
    {
        var executable = ResolveExecutable(project);
        if (!File.Exists(executable))
        {
            throw new GameExecutableNotFoundException(executable);
        }

        _starter.Start(executable, Path.GetFullPath(project.LoadConfig().Game.Path, project.Root));
        return executable;
    }
}
