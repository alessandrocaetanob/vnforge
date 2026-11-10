using VNForge.Core.Engines;

namespace VNForge.Core.Library;

/// <summary>
/// O que abrir para jogar. <paramref name="Executable"/> é o programa que o sistema inicia: o do jogo, ou o <c>LEProc.exe</c> do Locale Emulator, que então
/// recebe o do jogo em <paramref name="Arguments"/>. <paramref name="GameExecutable"/> é sempre o do jogo. <paramref name="OpensLauncher"/> diz que o executável aberto é um lançador, e o jogo é outro processo.
/// </summary>
public sealed record GameLaunch(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory, string GameExecutable, bool ViaLocaleEmulator, bool OpensLauncher = false);

/// <summary>Monta o <see cref="GameLaunch"/> de um jogo da biblioteca. Só lê o disco para conferir que os executáveis existem; não grava nada na pasta do jogo.</summary>
public static class GameLaunchPlanner
{
    public const string LocaleEmulatorExecutable = "LEProc.exe";

    /// <param name="library">A biblioteca, que guarda onde está o Locale Emulator.</param>
    /// <param name="game">O jogo a abrir.</param>
    /// <param name="japaneseLocale">Força a localidade japonesa nesta abertura; <see langword="null"/> segue a opção do jogo.</param>
    /// <exception cref="LibraryException">O jogo não tem executável, o executável não existe ou sai da pasta, ou o Locale Emulator é pedido e não está configurado.</exception>
    public static GameLaunch Plan(GameLibrary library, LibraryGame game, bool? japaneseLocale = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(game);

        var executable = ResolveGameExecutable(game);
        if (!(japaneseLocale ?? game.RequiresJapaneseLocale))
        {
            return new GameLaunch(executable, [], Path.GetDirectoryName(executable)!, executable, ViaLocaleEmulator: false, game.OpensLauncher);
        }

        var launcher = ResolveLocaleEmulator(library.LocaleEmulatorPath);

        // Sintaxe do LEProc: "LEProc.exe <caminho absoluto do jogo>" abre com o perfil do próprio jogo (<jogo>.exe.le.config), senão com o primeiro perfil global,
        // senão com ja-JP. "-run" sem o .le.config abriria a janela de configuração do Locale Emulator em vez do jogo, por isso não é usado.
        // Fonte: LEProc/Program.cs em https://github.com/xupefei/Locale-Emulator (texto de uso: "path  Run an application with (i) its own profile (if any) or
        // (ii) first global profile (if any) or (iii) default ja-JP profile").
        return new GameLaunch(launcher, [executable], Path.GetDirectoryName(executable)!, executable, ViaLocaleEmulator: true, game.OpensLauncher);
    }

    private static string ResolveGameExecutable(LibraryGame game)
    {
        if (string.IsNullOrWhiteSpace(game.Exe))
        {
            throw new LibraryException($"'{game.Title}' não tem executável conhecido. Rode vnf library scan de novo na pasta do jogo, ou abra o jogo pela pasta.");
        }

        string executable;
        try
        {
            executable = GamePaths.Resolve(game.Folder, game.Exe);
        }
        catch (ArgumentException e)
        {
            throw new LibraryException($"O executável '{game.Exe}' de '{game.Title}' não fica dentro da pasta do jogo. {e.Message}", e);
        }

        if (!File.Exists(executable))
        {
            throw new LibraryException($"O executável de '{game.Title}' não foi encontrado: {executable}. A pasta mudou de lugar? Rode vnf library scan de novo.");
        }

        return executable;
    }

    /// <summary>O <c>LEProc.exe</c> que o caminho configurado aponta (ele mesmo, ou a pasta que o contém).</summary>
    /// <exception cref="LibraryException">O caminho está vazio ou não leva a um <c>LEProc.exe</c> que existe.</exception>
    public static string ResolveLocaleEmulator(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new LibraryException("Este jogo pede a localidade japonesa, mas o Locale Emulator não está configurado. Informe o caminho com vnf library emulator <pasta ou LEProc.exe>.");
        }

        var path = Path.GetFullPath(configured.Trim().Trim('"'));
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, LocaleEmulatorExecutable);
        }

        if (!File.Exists(path))
        {
            throw new LibraryException($"O Locale Emulator não foi encontrado em {path}. Confira o caminho com vnf library emulator.");
        }

        return path;
    }
}
