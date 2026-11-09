using VNForge.Core.Projects;

namespace VNForge.Core.Library;

/// <summary>O que a troca fez na pasta do jogo.</summary>
public enum LibraryTranslationAction
{
    /// <summary>O jogo já estava como pedido; nada foi gravado.</summary>
    Unchanged,

    /// <summary>Os originais do backup voltaram para a pasta do jogo.</summary>
    ReturnedToOriginal,

    /// <summary>A tradução inteira do projeto foi copiada para a pasta do jogo, com backup.</summary>
    AppliedTranslation,
}

/// <summary>O resultado da troca: o que o jogo era, o que fez e o que é agora.</summary>
public sealed record LibraryTranslationSwitch(LibraryTranslationAction Action, LibraryTranslationState Before, LibraryTranslationState After);

/// <summary>
/// Troca um jogo da biblioteca entre o original e o traduzido com um comando. Não grava nada no jogo por conta própria: a volta é a restauração do backup
/// (<see cref="GameTester.ReturnToOriginal"/>, a mesma de <c>vnf restore</c>) e a ida é o teste no jogo sem abrir o executável (<see cref="GameTester.Run"/>: conferir, montar
/// o <c>build/</c>, backup, injetar), então a regra 2 do CLAUDE.md vale como em qualquer outra injeção. Só troca quando o estado é conhecido.
/// </summary>
public sealed class LibraryTranslationSwitcher(GameTester tester)
{
    private readonly GameTester _tester = tester ?? throw new ArgumentNullException(nameof(tester));

    /// <summary>Devolve os arquivos originais à pasta do jogo. A tradução continua no projeto.</summary>
    /// <exception cref="LibraryException">O estado do jogo é desconhecido (sem projeto, projeto de outra pasta, backup que sumiu).</exception>
    /// <exception cref="Injection.InjectionException">O backup não confere, ou o jogo mudou depois da injeção: nada foi gravado.</exception>
    public LibraryTranslationSwitch ToOriginal(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var project = LibraryTranslationReader.OpenProject(game);
        var before = LibraryTranslationReader.Read(game);
        if (before.Kind == LibraryTranslationKind.Original)
        {
            return new LibraryTranslationSwitch(LibraryTranslationAction.Unchanged, before, before);
        }

        _tester.ReturnToOriginal(project);
        return new LibraryTranslationSwitch(LibraryTranslationAction.ReturnedToOriginal, before, LibraryTranslationReader.Read(game));
    }

    /// <summary>
    /// Põe a tradução inteira do projeto na pasta do jogo. Um jogo que só tem o teste de acentos ou uma cena também recebe a tradução inteira; o original continua
    /// o do primeiro backup. Já traduzido por inteiro, não grava nada.
    /// </summary>
    /// <param name="steps">Recebe o passo que está começando.</param>
    /// <param name="scripts">Andamento do build, script a script.</param>
    /// <exception cref="LibraryException">O estado do jogo é desconhecido.</exception>
    /// <exception cref="QaBlockedException">A Conferência achou erro ou aviso: nada foi gravado.</exception>
    /// <exception cref="VNForgeException">Falha de build ou de injeção; a mensagem diz se o jogo voltou ao estado original.</exception>
    public LibraryTranslationSwitch ToTranslated(LibraryGame game, IProgress<GameTestStep>? steps = null, IProgress<ScriptWorkProgress>? scripts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        var project = LibraryTranslationReader.OpenProject(game);
        var before = LibraryTranslationReader.Read(game);
        if (before.Patch is { Kind: GameTestKind.Translation, ScriptKey: null })
        {
            return new LibraryTranslationSwitch(LibraryTranslationAction.Unchanged, before, before);
        }

        _tester.Run(project, new GameTestOptions(OpenGame: false), steps, scripts, cancellationToken);
        return new LibraryTranslationSwitch(LibraryTranslationAction.AppliedTranslation, before, LibraryTranslationReader.Read(game));
    }
}
