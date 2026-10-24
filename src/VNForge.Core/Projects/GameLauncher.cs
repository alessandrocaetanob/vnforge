using System.Diagnostics;
using VNForge.Core.Engines;
using VNForge.Core.Injection;

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

    /// <summary>
    /// Caminho completo de <c>game.exe</c>; <c>game.path</c> é relativo à pasta do projeto quando não é absoluto.
    /// O <c>vnforge.json</c> pode vir de um projeto compartilhado, e o arquivo é aberto pelo shell. Por isso valem duas regras:
    /// <c>game.path</c> tem de ser a pasta que este projeto já registrou, na extração (<c>.cache/state.json</c>) ou numa injeção
    /// (manifesto em <c>backup/</c>), então <c>C:\Windows\System32</c> não passa só por estar no JSON; e <c>game.exe</c> tem de ser um
    /// <c>.exe</c> relativo a essa pasta e dentro dela (nada de caminho absoluto, <c>..</c>, <c>.bat</c> ou atalho).
    /// Não protege de uma pasta de projeto adulterada por inteiro, com <c>.cache/</c> ou <c>backup/</c> forjados.
    /// </summary>
    /// <exception cref="VNForgeException"><c>game.path</c> não é a pasta registrada pelo projeto, ou <c>game.exe</c> é absoluto, sai da pasta do jogo ou não termina em <c>.exe</c>.</exception>
    public static string ResolveExecutable(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var game = project.LoadConfig().Game;
        var gameDirectory = Path.GetFullPath(game.Path, project.Root);
        EnsureRegistered(project, game.Path, gameDirectory);
        string executable;
        try
        {
            executable = GamePaths.Resolve(gameDirectory, game.Exe);
        }
        catch (ArgumentException ex)
        {
            throw new VNForgeException($"O game.exe do {ProjectFolder.ConfigFileName} ('{game.Exe}') precisa ser um caminho relativo à pasta do jogo e ficar dentro dela. {ex.Message}", ex);
        }

        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new VNForgeException($"O game.exe do {ProjectFolder.ConfigFileName} ('{game.Exe}') não é um executável .exe, e o VNForge só abre arquivos .exe da pasta do jogo.");
        }

        return executable;
    }

    private static void EnsureRegistered(ProjectFolder project, string configured, string gameDirectory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var wanted = Normalize(gameDirectory);
        var registered = new List<string>();
        if (project.LoadState().GameDirectory is { } extracted)
        {
            registered.Add(extracted);
        }

        registered.AddRange(GameInjector.RecordedGameDirectories(project));
        if (!registered.Any(r => string.Equals(Normalize(r), wanted, comparison)))
        {
            throw new VNForgeException(
                $"O game.path do {ProjectFolder.ConfigFileName} ('{configured}') não é uma pasta de jogo que este projeto registrou, então o VNForge não abre o executável dela. "
                + "Rode vnf extract (ou o assistente de novo projeto) com a pasta do jogo nesta máquina antes de abrir o jogo.");
        }
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
