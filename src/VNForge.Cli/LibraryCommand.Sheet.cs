using System.CommandLine;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Sheet;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf library sheet fetch|translate|show|review|approve|edit|apply</c>: a ficha do jogo em português (d9). <c>fetch</c> traz sinopse, tags e personagens do VNDB,
/// <c>translate</c> os traduz pela IA, <c>review</c> e <c>approve</c> levam cada texto pelas etapas de uma fala, <c>show</c> mostra a ficha com o spoiler e o conteúdo adulto
/// escondidos, e <c>apply</c> leva os personagens e a sinopse para o projeto de tradução do jogo. As fichas ficam em <c>sheets/</c>, ao lado do <c>library.json</c>.
/// </summary>
internal static partial class LibraryCommand
{
    private static readonly Option<bool> SheetSpoilersOption = new("--show-spoilers")
    {
        Description = "Mostra os spoilers da ficha (trechos da sinopse e das descrições, personagens, traços, tags e o gênero real) só nesta chamada.",
    };

    private static readonly Option<bool> SheetAdultOption = new("--show-adult")
    {
        Description = "Mostra retratos, tags e traços de conteúdo adulto da ficha só nesta chamada.",
    };

    private static readonly Option<bool> SheetIdsOption = new("--ids")
    {
        Description = "Mostra o id de cada texto (character:c12, tag:g7, trait:i34), que review, approve e edit aceitam.",
    };

    private static readonly Option<bool> SheetForceOption = new("--force")
    {
        Description = "Refaz só o que a IA já traduziu; o que você escreveu, revisou ou aprovou fica, mesmo desatualizado.",
    };

    private static readonly Option<bool> IncludeMachineOption = new("--include-machine")
    {
        Description = "Leva também o que a IA traduziu e ninguém revisou (por padrão só vai o texto revisado ou aprovado).",
    };

    private static Command CreateSheet(VnfCli cli)
    {
        var command = new Command("sheet", "A ficha do jogo em português: sinopse, tags e personagens do VNDB, traduzidos e revisados.");
        command.Subcommands.Add(CreateSheetFetch(cli));
        command.Subcommands.Add(CreateSheetTranslate(cli));
        command.Subcommands.Add(CreateSheetShow(cli));
        command.Subcommands.Add(CreateSheetMark(cli, "review", "Marca como revisados os textos da ficha (sem textos, os traduzidos que não estão desatualizados).", (store, id, fields) => SheetEditor.Review(store, id, fields), "revisados", "ainda não tinham tradução ou já estavam revisados"));
        command.Subcommands.Add(CreateSheetMark(cli, "approve", "Marca como aprovados os textos revisados da ficha (sem textos, todos os revisados).", (store, id, fields) => SheetEditor.Approve(store, id, fields), "aprovados", "ainda não foram revisados ou já estavam aprovados"));
        command.Subcommands.Add(CreateSheetEdit(cli));
        command.Subcommands.Add(CreateSheetApply(cli));
        return command;
    }

    private static Argument<string> SheetGameArgument() => new("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título) com o jogo do VNDB confirmado." };

    private static SheetStore SheetStoreOf(VnfCli cli) => new(cli.UserConfigDirectory);

    private static (LibraryGame Game, string VndbId, GameLibrary Library) SheetGameOf(VnfCli cli, string reference)
    {
        var library = FileOf(cli).Load();
        var game = library.Find(reference);
        return game.VndbId is { } id
            ? (game, id, library)
            : throw new LibraryException($"'{game.Title}' ainda não tem jogo do VNDB confirmado. Use vnf library match.");
    }

    private static Command CreateSheetFetch(VnfCli cli)
    {
        var game = SheetGameArgument();
        var command = new Command("fetch", "Traz do VNDB a sinopse, as tags e os personagens do jogo; buscar de novo mantém as traduções.");
        command.Arguments.Add(game);
        command.SetAction((parseResult, token) => SheetFetchAsync(cli, parseResult, parseResult.GetRequiredValue(game), token));
        return command;
    }

    private static async Task<int> SheetFetchAsync(VnfCli cli, ParseResult parseResult, string reference, CancellationToken token)
    {
        var (game, vndbId, _) = SheetGameOf(cli, reference);
        var result = await SheetFetcher.FetchAsync(cli.CreateVndbCatalog(), SheetStoreOf(cli), vndbId, cli.Clock, token).ConfigureAwait(false);
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new
            {
                game = new { id = game.Id, title = game.Title, vndbId },
                characters = result.Characters,
                tags = result.Sheet.Tags.Count,
                newTerms = result.NewTerms,
                charactersTruncated = result.Sheet.CharactersTruncated,
            })).ConfigureAwait(false);
            return ErrorHandler.OkExitCode;
        }

        await output.WriteLineAsync($"{game.Title}: ficha buscada no VNDB ({vndbId}): {CliFormat.Count(result.Characters, "personagem", "personagens")}, {CliFormat.Count(result.Sheet.Tags.Count, "tag", "tags")}.").ConfigureAwait(false);
        if (result.Sheet.CharactersTruncated)
        {
            await output.WriteLineAsync("O VNDB tem mais personagens do que o limite que o cliente traz; os últimos ficaram de fora.").ConfigureAwait(false);
        }

        await output.WriteLineAsync($"{CliFormat.Count(result.NewTerms, "nome de tag ou traço novo", "nomes de tag e traço novos")} para traduzir. Traduza com vnf library sheet translate {reference}.").ConfigureAwait(false);
        await output.WriteLineAsync(VndbCredit.ShortText).ConfigureAwait(false);
        return ErrorHandler.OkExitCode;
    }

    private static Command CreateSheetTranslate(VnfCli cli)
    {
        var game = SheetGameArgument();
        var ai = new AiOptions();
        var command = new Command("translate", "Traduz pela IA o que falta da ficha: sinopse, personagens e nomes de tag e de traço.");
        command.Arguments.Add(game);
        command.Options.Add(ai.Model);
        command.Options.Add(ai.Effort);
        command.Options.Add(SheetForceOption);
        command.Options.Add(ai.DryRun);
        command.SetAction((parseResult, token) => SheetTranslateAsync(cli, parseResult, ai, parseResult.GetRequiredValue(game), token));
        return command;
    }

    private static async Task<int> SheetTranslateAsync(VnfCli cli, ParseResult parseResult, AiOptions ai, string reference, CancellationToken token)
    {
        var (game, vndbId, _) = SheetGameOf(cli, reference);
        var store = SheetStoreOf(cli);
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);
        var settings = AiCommandSupport.ReadSettings(cli, AiConfig.Default, parseResult.GetValue(ai.Model), parseResult.GetValue(ai.Effort), null, null);
        var options = new SheetTranslationOptions { Force = parseResult.GetValue(SheetForceOption) };
        var plan = SheetTranslationRunner.Plan(store, vndbId, options.Force);

        if (plan.IsEmpty)
        {
            WriteSheetResult(output, json, new SheetRun(game, settings, plan, null, null, null, DryRun: false));
            return ErrorHandler.OkExitCode;
        }

        var estimate = SheetCost.Estimate(plan, settings.Model, options, settings.Effort);
        var budget = TranslateCommand.ReadBudget(parseResult, cli, error);
        if (parseResult.GetValue(ai.DryRun))
        {
            WriteSheetResult(output, json, new SheetRun(game, settings, plan, estimate, null, null, DryRun: true, budget.Rate));
            return ErrorHandler.OkExitCode;
        }

        var brl = estimate.Brl(budget.Rate);
        if (budget.Limits.WarnAbove is { } warn && brl > warn
            && !ConfirmationPrompt.Confirm(parseResult, cli, $"Traduzir a ficha custa cerca de {AiCommandSupport.Brl(brl)}, acima do valor de aviso de {AiCommandSupport.Brl(warn)}. Continuar?"))
        {
            await output.WriteLineAsync(json ? JsonEnvelope.Result(new { budgetStop = "declined" }, ErrorHandler.ErrorExitCode) : "O pedido não foi confirmado: nada foi enviado à IA.").ConfigureAwait(false);
            return ErrorHandler.ErrorExitCode;
        }

        ISheetTranslator translator = cli.SheetTranslatorFactory is { } factory
            ? factory(settings.Model, settings.Effort)
            : ClaudeProviderFactory.CreateSheet(AiCommandSupport.RequireApiKey(cli), new ClaudeOptions { Model = settings.Model, Effort = settings.Effort });
        var progress = json ? null : new SyncProgress<SheetTranslationProgress>(p => error.WriteLine($"{p.Done}/{p.Total} itens"));
        var result = await new SheetTranslationRunner(translator, store).RunAsync(vndbId, options, progress, token).ConfigureAwait(false);
        var usage = (translator as ClaudeSheetTranslator)?.Usage;
        WriteSheetResult(output, json, new SheetRun(game, settings, plan, estimate, result, usage, DryRun: false, budget.Rate));
        return result.Failed > 0 ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
    }

    private sealed record SheetRun(
        LibraryGame Game,
        AiRunSettings Settings,
        SheetTranslationPlan Plan,
        SheetCostEstimate? Estimate,
        SheetTranslationResult? Result,
        ClaudeUsage? Usage,
        bool DryRun,
        ExchangeRate? Rate = null);

    private static void WriteSheetResult(TextWriter output, bool json, SheetRun run)
    {
        var (game, settings, plan, estimate, result, usage, dryRun, rate) = run;
        var dollars = RunCostTracker.DollarsFor(settings.Model, usage);
        if (json)
        {
            var brl = estimate is not null && rate is not null ? estimate.Brl(rate) : (decimal?)null;
            output.WriteLine(JsonEnvelope.Result(
                new
                {
                    dryRun,
                    game = new { id = game.Id, title = game.Title },
                    model = settings.Model,
                    effort = settings.Effort.ToString().ToLowerInvariant(),
                    pending = new
                    {
                        synopsis = plan.Count(SheetItemKind.Synopsis),
                        characters = plan.Count(SheetItemKind.CharacterDescription),
                        tags = plan.Count(SheetItemKind.TagName),
                        traits = plan.Count(SheetItemKind.TraitName),
                    },
                    estimate = estimate is null ? null : new { calls = estimate.Calls, dollars = estimate.Dollars, brl },
                    translated = result?.Translated,
                    calls = result?.Calls,
                    failed = result?.Failures.Select(f => new { keys = f.Keys, error = f.Message }),
                    usage = AiCommandSupport.UsageJson(usage),
                    dollars,
                },
                result is { Failed: > 0 } ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode));
            return;
        }

        if (plan.IsEmpty)
        {
            output.WriteLine($"{game.Title}: nada a traduzir na ficha.");
            return;
        }

        var parts = $"{CliFormat.Count(plan.Count(SheetItemKind.Synopsis), "sinopse", "sinopses")}, {CliFormat.Count(plan.Count(SheetItemKind.CharacterDescription), "descrição de personagem", "descrições de personagem")}, "
            + $"{CliFormat.Count(plan.Count(SheetItemKind.TagName), "nome de tag", "nomes de tag")}, {CliFormat.Count(plan.Count(SheetItemKind.TraitName), "nome de traço", "nomes de traço")}";
        if (dryRun)
        {
            output.WriteLine($"Ficha de {game.Title} ({settings.Model}): falta traduzir {parts}.");
            output.WriteLine($"Custo estimado {AiCommandSupport.Brl(estimate!.Brl(rate!))} ({AiCommandSupport.Dollars(estimate.Dollars)}) em {CliFormat.Count(estimate.Calls, "chamada", "chamadas")}.");
            AiCommandSupport.WriteEstimateTail(output, estimate.Usage, estimate.Dollars, "vnf library sheet translate");
            return;
        }

        output.WriteLine($"{game.Title}: {CliFormat.Count(result!.Translated, "texto traduzido", "textos traduzidos")} pela IA em {CliFormat.Count(result.Calls, "chamada", "chamadas")} (de {parts}).");
        foreach (var failure in result.Failures)
        {
            output.WriteLine($"Ficou sem tradução ({string.Join(", ", failure.Keys.Take(5))}{(failure.Keys.Count > 5 ? ", ..." : string.Empty)}): {failure.Message}");
        }

        if (result.Failed > 0)
        {
            output.WriteLine($"{CliFormat.Count(result.Failed, "texto segue", "textos seguem")} pendente: rode o comando de novo para tentar outra vez.");
        }

        AiCommandSupport.WriteUsage(output, usage, dollars);
        output.WriteLine($"Revise com vnf library sheet review {game.Id}; o que a IA traduziu só vale para a tradução depois de revisado.");
    }

    private static Command CreateSheetShow(VnfCli cli)
    {
        var game = SheetGameArgument();
        var command = new Command("show", "Mostra a ficha em português, com spoiler e conteúdo adulto escondidos.");
        command.Arguments.Add(game);
        command.Options.Add(SheetSpoilersOption);
        command.Options.Add(SheetAdultOption);
        command.Options.Add(SheetIdsOption);
        command.SetAction(parseResult => SheetShow(cli, parseResult, parseResult.GetRequiredValue(game)));
        return command;
    }

    private static int SheetShow(VnfCli cli, ParseResult parseResult, string reference)
    {
        var (game, vndbId, library) = SheetGameOf(cli, reference);
        var store = SheetStoreOf(cli);
        var sheet = store.LoadGame(vndbId) ?? throw new LibraryException($"{game.Title} ainda não tem ficha. Busque-a com vnf library sheet fetch {reference}.");
        var privacy = new LibraryPrivacy(
            library.Privacy.ShowSpoilers || parseResult.GetValue(SheetSpoilersOption),
            parseResult.GetValue(SheetAdultOption) ? AdultMode.Show : library.Privacy.Adult);
        var card = SheetViewer.Card(sheet, store.LoadCharacters(sheet.CharacterIds), store.LoadTerms(), privacy);
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = new { id = game.Id, title = game.Title }, sheet = card with { Characters = [.. card.Characters.Select(c => c.ImageHidden ? c with { Image = null } : c)] } }));
            return ErrorHandler.OkExitCode;
        }

        WriteSheet(output, card, privacy, parseResult.GetValue(SheetIdsOption));
        return ErrorHandler.OkExitCode;
    }

    private static void WriteSheet(TextWriter output, GameSheetCard card, LibraryPrivacy privacy, bool ids)
    {
        static string Id(bool show, string key) => show ? $" [{key}]" : string.Empty;

        output.WriteLine($"{card.Title} ({card.VisualNovelId}){(card.OriginalTitle is { } original && original != card.Title ? $" · {original}" : string.Empty)}");
        output.WriteLine($"  Duração: {card.Length}");
        if (card.Synopsis is { } synopsis)
        {
            output.WriteLine($"  Sinopse ({SheetState(synopsis)}){Id(ids, SheetField.Synopsis.Key)}:");
            WriteIndented(output, synopsis.Text, "    ");
            if (synopsis.HasHiddenSpoilers)
            {
                output.WriteLine("    (trechos de spoiler escondidos; use --show-spoilers)");
            }
        }

        output.WriteLine("  Tags: " + (card.Tags.Count == 0 ? "nenhuma" : string.Join(", ", card.Tags.Select(t => t.Name + Id(ids, SheetField.OfTag(t.Id).Key))))
            + (card.HiddenTags > 0 ? $" ({CliFormat.Count(card.HiddenTags, "tag escondida", "tags escondidas")}; spoiler e conteúdo adulto)" : string.Empty));
        output.WriteLine($"  Personagens: {card.Characters.Count}{(card.HiddenCharacters > 0 ? $" (mais {CliFormat.Count(card.HiddenCharacters, "escondido", "escondidos")} por spoiler; use --show-spoilers)" : string.Empty)}"
            + (card.CharactersTruncated ? "; o VNDB tem mais do que o limite trazido" : string.Empty));
        if (card.StaleCharacters > 0 && !privacy.ShowSpoilers)
        {
            output.WriteLine($"  Aviso: {CliFormat.Count(card.StaleCharacters, "personagem está escondido", "personagens estão escondidos")} porque a ficha é antiga e o nível de spoiler deles é desconhecido. Busque de novo com vnf library sheet fetch <jogo>.");
        }

        foreach (var character in card.Characters)
        {
            output.WriteLine($"    {character.Name}{(character.Original is { } name ? $" ({name})" : string.Empty)}{Id(ids, SheetField.OfCharacter(character.Id).Key)} · {character.Role} · {character.Gender}"
                + (character.RealGender is { } real ? $" (real: {real})" : string.Empty));
            if (character.ImageHidden)
            {
                output.WriteLine("      Retrato: escondido (conteúdo adulto; use --show-adult)");
            }
            else if (character.Image is { } image)
            {
                output.WriteLine($"      Retrato: {image.Url}");
            }

            if (character.Description is { } description)
            {
                output.WriteLine($"      Descrição ({SheetState(description)}):");
                WriteIndented(output, description.Text, "        ");
            }

            if (character.Traits.Count > 0 || character.HiddenTraits > 0)
            {
                output.WriteLine("      Traços: " + (character.Traits.Count == 0 ? "nenhum à vista" : string.Join(", ", character.Traits.Select(t => t.Name + Id(ids, SheetField.OfTrait(t.Id).Key))))
                    + (character.HiddenTraits > 0 ? $" ({CliFormat.Count(character.HiddenTraits, "escondido", "escondidos")})" : string.Empty));
            }
        }

        var counts = card.Counts;
        output.WriteLine($"  Textos: {counts.Pending} sem tradução, {counts.Machine} traduzidos pela IA, {counts.Translated} à mão, {counts.Reviewed} revisados, {counts.Approved} aprovados.");
        if (!privacy.ShowSpoilers || !privacy.ShowAdult)
        {
            output.WriteLine("  Spoiler e conteúdo adulto escondidos por padrão; --show-spoilers e --show-adult liberam só esta chamada.");
        }

        output.WriteLine("  " + VndbCredit.ShortText);
    }

    private static string SheetState(SheetShownText text) =>
        (text.Translated ? SheetLabels.Status(text.Status) : "só o original em inglês, ainda sem tradução") + (text.Outdated ? "; o VNDB mudou o original, revise de novo" : string.Empty);

    private static void WriteIndented(TextWriter output, string text, string indent)
    {
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            output.WriteLine(indent + line);
        }
    }

    private static Command CreateSheetMark(
        VnfCli cli, string name, string description, Func<SheetStore, string, IReadOnlyList<SheetField>?, SheetMarkResult> change, string past, string unchangedWhy)
    {
        var game = SheetGameArgument();
        var fields = new Argument<string[]>("texto")
        {
            Description = "Os textos: synopsis, character:c12 (ou c12), tag:g7, trait:i34. Sem eles, todos os que podem passar a esta etapa. Veja os ids com vnf library sheet show --ids.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command(name, description);
        command.Arguments.Add(game);
        command.Arguments.Add(fields);
        command.SetAction(parseResult =>
        {
            var (found, vndbId, _) = SheetGameOf(cli, parseResult.GetRequiredValue(game));
            var asked = parseResult.GetValue(fields) is { Length: > 0 } values ? values.Select(SheetField.Parse).ToList() : null;
            var result = change(SheetStoreOf(cli), vndbId, asked);
            var output = parseResult.InvocationConfiguration.Output;
            if (parseResult.GetValue(cli.Options.Json))
            {
                output.WriteLine(JsonEnvelope.Result(new { game = new { id = found.Id, title = found.Title }, changed = result.Changed, unchanged = result.Unchanged, skippedOutdated = result.SkippedOutdated }));
            }
            else
            {
                output.WriteLine($"{found.Title}: {CliFormat.Count(result.Changed, "texto", "textos")} {past}; {result.Unchanged} {unchangedWhy}.");
                if (result.SkippedOutdated > 0)
                {
                    output.WriteLine($"{CliFormat.Count(result.SkippedOutdated, "texto desatualizado ficou", "textos desatualizados ficaram")} de fora, porque o VNDB mudou o original depois do seu texto. Confira e revise citando o texto (vnf library sheet show {found.Id} --ids mostra os ids).");
                }
            }

            return ErrorHandler.OkExitCode;
        });
        return command;
    }

    private static Command CreateSheetEdit(VnfCli cli)
    {
        var game = SheetGameArgument();
        var field = new Argument<string>("texto") { Description = "O texto a corrigir: synopsis, character:c12 (ou c12), tag:g7 ou trait:i34." };
        var text = new Argument<string>("traducao") { Description = "A tradução. Precisa manter os mesmos trechos [spoiler]...[/spoiler] do original." };
        var command = new Command("edit", "Escreve ou corrige à mão a tradução de um texto da ficha; ele volta a pedir revisão.");
        command.Arguments.Add(game);
        command.Arguments.Add(field);
        command.Arguments.Add(text);
        command.SetAction(parseResult =>
        {
            var (found, vndbId, _) = SheetGameOf(cli, parseResult.GetRequiredValue(game));
            var target = SheetField.Parse(parseResult.GetRequiredValue(field));
            SheetEditor.Edit(SheetStoreOf(cli), vndbId, target, parseResult.GetRequiredValue(text));
            var output = parseResult.InvocationConfiguration.Output;
            if (parseResult.GetValue(cli.Options.Json))
            {
                output.WriteLine(JsonEnvelope.Result(new { game = new { id = found.Id, title = found.Title }, field = target.Key, status = "translated" }));
            }
            else
            {
                output.WriteLine($"{found.Title}: {target.Key} gravado como traduzido à mão. Revise com vnf library sheet review {found.Id} {target.Key}.");
            }

            return ErrorHandler.OkExitCode;
        });
        return command;
    }

    private static Command CreateSheetApply(VnfCli cli)
    {
        var game = SheetGameArgument();
        var command = new Command("apply", "Leva a ficha ao projeto do jogo: personagens para o Glossário e sinopse para a bíblia.");
        command.Arguments.Add(game);
        command.Options.Add(IncludeMachineOption);
        command.SetAction(parseResult =>
        {
            var (found, vndbId, _) = SheetGameOf(cli, parseResult.GetRequiredValue(game));
            var folder = parseResult.GetValue(cli.Options.Project) ?? found.ProjectPath
                ?? throw new LibraryException($"'{found.Title}' não está ligado a um projeto de tradução. Use --project <pasta> ou vnf library link.");
            var project = ProjectFolder.Open(Path.GetFullPath(folder, cli.CurrentDirectory));
            var store = SheetStoreOf(cli);
            var sheet = store.LoadGame(vndbId) ?? throw new LibraryException($"{found.Title} ainda não tem ficha. Busque-a com vnf library sheet fetch {parseResult.GetRequiredValue(game)}.");
            var minimum = parseResult.GetValue(IncludeMachineOption) ? TranslationStatus.Machine : TranslationStatus.Reviewed;
            var result = SheetProjectBridge.Apply(project, sheet, store.LoadCharacters(sheet.CharacterIds), minimum);
            var output = parseResult.InvocationConfiguration.Output;
            if (parseResult.GetValue(cli.Options.Json))
            {
                output.WriteLine(JsonEnvelope.Result(new
                {
                    game = new { id = found.Id, title = found.Title },
                    project = project.Root,
                    matched = result.Characters.Matched,
                    added = result.Characters.Added,
                    filled = result.Characters.Filled,
                    synopsisApplied = result.SynopsisApplied,
                    notReady = result.NotReady,
                    staleCharacters = result.StaleCharacters,
                }));
            }
            else
            {
                output.WriteLine($"{found.Title}: {CliFormat.Count(result.Characters.Matched, "personagem casou", "personagens casaram")} com o projeto ({result.Characters.Added} novos, {result.Characters.Filled} completados).");
                output.WriteLine(result.SynopsisApplied ? "Sinopse levada para a bíblia." : "A bíblia já tinha sinopse (ou não há sinopse pronta); nada mudou nela.");
                if (result.StaleCharacters > 0)
                {
                    output.WriteLine($"{CliFormat.Count(result.StaleCharacters, "personagem ficou", "personagens ficaram")} sem descrição: a ficha é antiga e o nível de spoiler deles é desconhecido. Busque de novo com vnf library sheet fetch {parseResult.GetRequiredValue(game)}.");
                }

                if (result.NotReady > 0)
                {
                    output.WriteLine($"{CliFormat.Count(result.NotReady, "texto ficou", "textos ficaram")} de fora por ainda não {(minimum == TranslationStatus.Reviewed ? "estar revisado" : "ter tradução")}. Revise com vnf library sheet review {found.Id}.");
                }
            }

            return ErrorHandler.OkExitCode;
        });
        return command;
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
