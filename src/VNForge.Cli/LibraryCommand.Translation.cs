using System.CommandLine;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.Cli;

/// <summary><c>vnf library state|switch</c>: qual jogo está com o patch do VNForge, e a troca entre o original e o traduzido pela injeção e pelo backup do projeto ligado.</summary>
internal static partial class LibraryCommand
{
    private static readonly Option<bool> OriginalOption = new("--original")
    {
        Description = "Devolve os arquivos originais à pasta do jogo (a restauração do backup, como vnf restore).",
    };

    private static readonly Option<bool> TranslatedOption = new("--traduzido")
    {
        Description = "Põe a tradução inteira do projeto na pasta do jogo, guardando antes os originais (a injeção, como vnf test --no-open).",
    };

    private static Command CreateState(VnfCli cli)
    {
        var game = new Argument<string?>("jogo")
        {
            Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título). Sem ele, mostra todos.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("state", "Mostra se cada jogo está original, traduzido ou desconhecido, pelo projeto ligado a ele.");
        command.Arguments.Add(game);
        command.SetAction(parseResult => State(cli, parseResult, parseResult.GetValue(game)));
        return command;
    }

    private static Command CreateSwitch(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("switch", "Troca o jogo entre o original e o traduzido, pelo backup e pela injeção do projeto ligado.");
        command.Arguments.Add(game);
        command.Options.Add(OriginalOption);
        command.Options.Add(TranslatedOption);
        command.SetAction((parseResult, token) => Switch(cli, parseResult, parseResult.GetRequiredValue(game), token));
        return command;
    }

    private static int State(VnfCli cli, ParseResult parseResult, string? reference)
    {
        var library = FileOf(cli).Load();
        var games = reference is null ? [.. library.Games.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)] : new List<LibraryGame> { library.Find(reference) };
        var states = games.Select(g => (Game: g, State: LibraryTranslationReader.Read(g))).ToList();
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { games = states.Select(s => TranslationData(s.Game, s.State)) }));
            return ErrorHandler.OkExitCode;
        }

        if (states.Count == 0)
        {
            output.WriteLine("A biblioteca está vazia. Use vnf library scan <pasta>.");
            return ErrorHandler.OkExitCode;
        }

        foreach (var (game, state) in states)
        {
            output.WriteLine($"{game.Title} ({game.Id}): {DescribeTranslation(state)}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static async Task<int> Switch(VnfCli cli, ParseResult parseResult, string reference, CancellationToken token)
    {
        var toOriginal = parseResult.GetValue(OriginalOption);
        var toTranslated = parseResult.GetValue(TranslatedOption);
        if (toOriginal == toTranslated)
        {
            throw new UsageException("Escolha o destino com --original (devolve os arquivos do jogo) ou --traduzido (põe a tradução do projeto), e só um dos dois.");
        }

        var json = parseResult.GetValue(cli.Options.Json);
        var output = parseResult.InvocationConfiguration.Output;
        var game = FileOf(cli).Load().Find(reference);
        var current = LibraryTranslationReader.Read(game);
        var switcher = new LibraryTranslationSwitcher(new GameTester(KnownEngines.All, cli.GameProcessStarter, cli.Qa));

        // Estado desconhecido não pergunta: o switcher recusa com o motivo. Quem já está no destino não grava nada, então também não pergunta.
        var alreadyThere = toOriginal
            ? current.Kind == LibraryTranslationKind.Original
            : current.Patch is { Kind: GameTestKind.Translation, ScriptKey: null };
        if (current.Kind != LibraryTranslationKind.Unknown && !alreadyThere)
        {
            var question = toOriginal
                ? $"Devolver os arquivos originais de {game.Title} à pasta do jogo?"
                : $"Gravar a tradução na pasta de {game.Title}, depois de guardar os originais?";
            if (!ConfirmationPrompt.Confirm(parseResult, cli, question))
            {
                output.WriteLine("Operação cancelada. Nada foi gravado.");
                return ErrorHandler.CancelledExitCode;
            }
        }

        LibraryTranslationSwitch result;
        try
        {
            result = toOriginal
                ? switcher.ToOriginal(game)
                : switcher.ToTranslated(game, new InlineProgress<GameTestStep>(step => ShowStep(parseResult, json, step)), cancellationToken: token);
        }
        catch (QaBlockedException blocked)
        {
            return BuildCommand.RunQaBlocked(json, blocked, output);
        }

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                game = new { id = game.Id, title = game.Title },
                action = ActionName(result.Action),
                before = TranslationData(game, result.Before),
                after = TranslationData(game, result.After),
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine(result.Action switch
        {
            LibraryTranslationAction.Unchanged => $"{game.Title}: já está como pedido ({DescribeTranslation(result.After)}). Nada foi gravado.",
            LibraryTranslationAction.ReturnedToOriginal => $"{game.Title}: devolvido ao original. A tradução continua no projeto.",
            _ => $"{game.Title}: com a tradução, {DescribeTranslation(result.After)}. Para voltar: vnf library switch {game.Id} --original",
        });
        return ErrorHandler.OkExitCode;
    }

    private static void ShowStep(ParseResult parseResult, bool json, GameTestStep step)
    {
        if (!json)
        {
            parseResult.InvocationConfiguration.Error.WriteLine($"[{(int)step + 1}/4] {step.Label()}");
        }
    }

    private static string ActionName(LibraryTranslationAction action) => action switch
    {
        LibraryTranslationAction.ReturnedToOriginal => "returnedToOriginal",
        LibraryTranslationAction.AppliedTranslation => "appliedTranslation",
        _ => "unchanged",
    };

    private static object TranslationData(LibraryGame game, LibraryTranslationState state) => new
    {
        id = game.Id,
        title = game.Title,
        state = state.Kind switch
        {
            LibraryTranslationKind.Original => "original",
            LibraryTranslationKind.Translated => "translated",
            _ => "unknown",
        },
        patch = state.Patch is { } patch
            ? new
            {
                kind = patch.Kind == GameTestKind.AccentTest ? "accentTest" : "translation",
                appliedAt = patch.AppliedAt,
                backup = patch.BackupDirectory,
                scene = patch.ScriptKey,
                textFingerprint = patch.TextFingerprint,
            }
            : null,
        reason = state.Reason,
    };

    private static string DescribeTranslation(LibraryTranslationState state)
    {
        switch (state.Kind)
        {
            case LibraryTranslationKind.Original:
                return "original";
            case LibraryTranslationKind.Translated:
                var patch = state.Patch!;
                var what = patch.Kind == GameTestKind.AccentTest
                    ? "teste de acentos"
                    : patch.ScriptKey is { } scene ? $"só a cena {scene}" : "tradução";
                var fingerprint = patch.TextFingerprint is { Length: > 0 } text ? $", textos {text[..Math.Min(8, text.Length)]}" : string.Empty;
                return $"traduzido ({what} de {patch.AppliedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PortugueseBrazil)}{fingerprint}, backup {patch.BackupDirectory})";
            default:
                return $"desconhecido: {state.Reason}";
        }
    }
}
