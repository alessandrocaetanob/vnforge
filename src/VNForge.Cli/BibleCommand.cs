using System.CommandLine;
using System.Text;
using Spectre.Console;
using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Bible;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using static VNForge.Cli.AiCommandSupport;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf bible</c>: a bíblia do jogo (<c>bible.json</c>). <c>generate</c> faz a IA ler o roteiro por capítulo e escrever, por cena, o resumo, quem aparece, o tom, as
/// piadas que voltam e as relações; <c>show</c>, <c>edit</c>, <c>export</c> e <c>undo</c> são os mesmos gestos da tela Bíblia do app. O resumo da cena entra em cada
/// pedido de <c>vnf translate</c> e <c>vnf review</c>.
/// </summary>
internal static class BibleCommand
{
    private const int SummaryColumnWidth = 60;

    private static readonly Option<string?> ChapterOption = new("--chapter")
    {
        Description = "Só alguns capítulos, pelo número ou pelo nome (veja 'vnf scripts list'); aceita vários, separados por vírgula. Sem a sinopse do jogo.",
        HelpName = "lista",
    };

    private static readonly Option<string?> ModelOption = new("--model")
    {
        Description = $"Modelo Claude: {string.Join(", ", ClaudeModels.All)}. Padrão: o de 'vnf config ai' ou o do vnforge.json.",
        HelpName = "modelo",
    };

    private static readonly Option<string?> EffortOption = new("--effort")
    {
        Description = "Esforço do modelo: low, medium ou high. Padrão: o do vnforge.json.",
        HelpName = "nível",
    };

    private static readonly Option<bool> DryRunOption = new("--dry-run")
    {
        Description = "Só mostra capítulos, cenas e o preço estimado; não chama a API nem grava nada.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("bible", "Gera e edita a bíblia do jogo (bible.json): o que a IA entendeu de cada cena.");
        command.Subcommands.Add(CreateGenerate(cli));
        command.Subcommands.Add(CreateShow(cli));
        command.Subcommands.Add(CreateEdit(cli));
        command.Subcommands.Add(CreateExport(cli));
        command.Subcommands.Add(CreateUndo(cli));
        return command;
    }

    private static Command CreateGenerate(VnfCli cli)
    {
        var command = new Command("generate", "A IA lê o roteiro por capítulo e escreve a bíblia; mostra o preço antes e poupa o que é seu.");
        command.Options.Add(ChapterOption);
        command.Options.Add(ModelOption);
        command.Options.Add(EffortOption);
        command.Options.Add(DryRunOption);
        command.SetAction((parseResult, token) => GenerateAsync(cli, parseResult, token));
        return command;
    }

    private static Command CreateShow(VnfCli cli)
    {
        var scene = new Argument<string?>("cena") { Description = "Chave da cena (yst00013). Sem ela, lista todas.", Arity = ArgumentArity.ZeroOrOne };
        var command = new Command("show", "Mostra a bíblia: a lista de cenas, ou todos os campos de uma cena.");
        command.Arguments.Add(scene);
        command.SetAction(parseResult => Show(cli, parseResult, parseResult.GetValue(scene)));
        return command;
    }

    private static Command CreateEdit(VnfCli cli)
    {
        var scene = new Argument<string?>("cena") { Description = "Chave da cena (yst00013). Com --game, não se usa.", Arity = ArgumentArity.ZeroOrOne };
        var game = new Option<bool>("--game") { Description = "Edita o jogo, não uma cena: use com --synopsis." };
        var synopsis = new Option<string?>("--synopsis") { Description = "A sinopse do jogo; só com --game.", HelpName = "texto" };
        var name = new Option<string?>("--name") { Description = "O nome da cena.", HelpName = "nome" };
        var summary = new Option<string?>("--summary") { Description = "O resumo da cena.", HelpName = "texto" };
        var characters = new Option<string?>("--characters") { Description = "Quem aparece, separado por vírgula (ou por ponto e vírgula).", HelpName = "lista" };
        var tone = new Option<string?>("--tone") { Description = "O tom da cena, separado por vírgula (ou por ponto e vírgula).", HelpName = "texto" };
        var jokes = new Option<string?>("--jokes") { Description = "As piadas que voltam, separadas por ponto e vírgula (ou por vírgula, se não houver ponto e vírgula).", HelpName = "lista" };
        var relation = new Option<string[]>("--relation")
        {
            Description = "Uma relação, no formato \"Kenta e Ren=Amigos de infância\"; repita a opção para várias. Troca todas as relações da cena.",
            HelpName = "par=nota",
            AllowMultipleArgumentsPerToken = false,
        };

        var command = new Command("edit", "Escreve campos da bíblia à mão; o campo editado passa a ser seu e a IA não o troca.");
        command.Arguments.Add(scene);
        command.Options.Add(game);
        command.Options.Add(synopsis);
        command.Options.Add(name);
        command.Options.Add(summary);
        command.Options.Add(characters);
        command.Options.Add(tone);
        command.Options.Add(jokes);
        command.Options.Add(relation);
        command.SetAction(parseResult => Edit(
            cli,
            parseResult,
            new EditRequest(
                parseResult.GetValue(scene),
                parseResult.GetValue(game),
                parseResult.GetValue(synopsis),
                parseResult.GetValue(name),
                parseResult.GetValue(summary),
                parseResult.GetValue(characters),
                parseResult.GetValue(tone),
                parseResult.GetValue(jokes),
                parseResult.GetValue(relation) ?? [])));
        return command;
    }

    private static Command CreateExport(VnfCli cli)
    {
        var output = new Option<string?>("--out") { Description = "Arquivo Markdown a gravar. Sem ele, escreve na saída.", HelpName = "arquivo" };
        var command = new Command("export", "Exporta a bíblia como texto Markdown, para ler fora do VNForge.");
        command.Options.Add(output);
        command.SetAction(parseResult => Export(cli, parseResult, parseResult.GetValue(output)));
        return command;
    }

    private static Command CreateUndo(VnfCli cli)
    {
        var command = new Command("undo", "Volta a bíblia ao que era antes da última geração; rodar de novo desfaz a volta.");
        command.SetAction(parseResult => Undo(cli, parseResult));
        return command;
    }

    private static async Task<int> GenerateAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);
        var project = Open(cli, parseResult);
        var settings = ReadSettings(cli, project.LoadConfig().Ai, parseResult.GetValue(ModelOption), parseResult.GetValue(EffortOption), null, null);

        var all = BibleChapterPlanner.Plan(project);
        if (all.Count == 0)
        {
            throw new UsageException("Não há cena com texto para a IA ler.", "Rode 'vnf extract' para ler o texto do jogo.");
        }

        var chapters = SelectChapters(all, parseResult.GetValue(ChapterOption));
        var includeOverview = chapters.Count == all.Count;
        var estimate = BibleCost.Estimate(chapters, settings.Model, effort: settings.Effort, includeOverview: includeOverview);
        var budget = TranslateCommand.ReadBudget(parseResult, cli, error);
        var brl = estimate.Brl(budget.Rate);

        if (parseResult.GetValue(DryRunOption))
        {
            return WriteDryRun(output, json, settings, estimate, budget.Rate, includeOverview);
        }

        if (!ConfirmationPrompt.Confirm(parseResult, cli, $"Gerar a bíblia custa cerca de {Brl(brl)} ({Count(estimate.Chapters, "capítulo", "capítulos")}, {Count(estimate.Scenes, "cena", "cenas")}, {settings.Model}). Continuar?"))
        {
            return WriteDeclined(output, json, settings, estimate);
        }

        var provider = cli.BibleProviderFactory is { } factory
            ? factory(settings.Model, settings.Effort)
            : ClaudeProviderFactory.CreateBible(RequireApiKey(cli), new ClaudeOptions { Model = settings.Model, Effort = settings.Effort });
        var tracker = new RunCostTracker(project, settings.Model, budget.Rate, () => (provider as ClaudeBibleProvider)?.Usage);
        var guard = tracker.CreateGuard(new BudgetLimits(budget.Limits.Limit, null), _ => brl, null);
        if (await guard.AuthorizeAsync(brl, token).ConfigureAwait(false) is { } refusal)
        {
            return WriteBudgetStop(output, json, refusal, budget.Limits.Limit);
        }

        var runner = new BibleRunner(provider, new BibleRunOptions { Model = settings.Model, Budget = guard, RunDollars = tracker.RunDollars });
        BibleRunResult result;
        try
        {
            result = await runner.RunAsync(project, chapters, json ? null : new LineProgress(error), token, includeOverview).ConfigureAwait(false);
        }
        finally
        {
            tracker.Record();
        }

        return WriteResult(output, json, settings, estimate, result, tracker);
    }

    private static int WriteDryRun(TextWriter output, bool json, AiRunSettings settings, BibleCostEstimate estimate, ExchangeRate rate, bool includeOverview)
    {
        if (json)
        {
            var data = EstimateJson(settings, estimate, rate, dryRun: true);
            data["overview"] = includeOverview;
            output.WriteLine(JsonEnvelope.Result(data));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Bíblia do jogo ({settings.Model}): {Count(estimate.Chapters, "capítulo", "capítulos")}, {Count(estimate.Scenes, "cena", "cenas")}, {Count(estimate.Lines, "linha", "linhas")} para a IA ler.");
        output.WriteLine($"Custo estimado {Brl(estimate.Brl(rate))} ({Dollars(estimate.Dollars)}); ler de novo um capítulo custa cerca de {Brl(estimate.RereadBrl(rate))}.");
        output.WriteLine($"~{Tokens(estimate.Usage.Input)} de entrada, ~{Tokens(estimate.Usage.CacheWrite)} escritos no cache de prompt, ~{Tokens(estimate.Usage.CacheRead)} lidos dele, ~{Tokens(estimate.Usage.Output)} de saída.");
        output.WriteLine("Nada foi enviado à IA. É uma ordem de grandeza: o custo real aparece ao fim de 'vnf bible generate'.");
        return ErrorHandler.OkExitCode;
    }

    private static int WriteDeclined(TextWriter output, bool json, AiRunSettings settings, BibleCostEstimate estimate)
    {
        if (json)
        {
            var data = EstimateJson(settings, estimate, ExchangeRate.Placeholder, dryRun: false);
            data["budgetStop"] = "declined";
            output.WriteLine(JsonEnvelope.Result(data, ErrorHandler.ErrorExitCode));
        }
        else
        {
            output.WriteLine("O pedido não foi confirmado: nenhuma cena foi enviada à IA.");
        }

        return ErrorHandler.ErrorExitCode;
    }

    private static int WriteBudgetStop(TextWriter output, bool json, BudgetStop stop, decimal? limit)
    {
        var reason = stop == BudgetStop.LimitReached ? "limitReached" : "declined";
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new { budgetStop = reason }, ErrorHandler.ErrorExitCode));
        }
        else
        {
            output.WriteLine(stop == BudgetStop.LimitReached
                ? $"O gasto do projeto já chegou ao limite{(limit is { } cap ? $" de {Brl(cap)}" : string.Empty)}; nenhuma cena foi enviada à IA. Aumente o limite nos Ajustes do app para continuar."
                : "O pedido não foi confirmado: nenhuma cena foi enviada à IA.");
        }

        return ErrorHandler.ErrorExitCode;
    }

    private static int WriteResult(TextWriter output, bool json, AiRunSettings settings, BibleCostEstimate estimate, BibleRunResult result, RunCostTracker tracker)
    {
        var failed = result.Failures.Count > 0 || result.BudgetStop is not null || result.Canceled;
        var exitCode = failed ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        var dollars = tracker.RunDollars();

        if (json)
        {
            var data = EstimateJson(settings, estimate, ExchangeRate.Placeholder, dryRun: false);
            data.Remove("estimate");
            data["scenesWritten"] = result.ScenesWritten;
            data["chaptersRead"] = result.ChaptersRead;
            data["overviewWritten"] = result.OverviewWritten;
            data["canceled"] = result.Canceled;
            data["budgetStop"] = result.BudgetStop switch
            {
                BudgetStop.LimitReached => "limitReached",
                BudgetStop.Declined => "declined",
                _ => null,
            };
            data["failed"] = result.Failures.Select(f => new { chapter = f.Chapter, error = f.Error.Message, attempts = f.Attempts });
            data["usage"] = UsageJson(tracker.Usage);
            data["dollars"] = dollars;
            output.WriteLine(JsonEnvelope.Result(data, exitCode));
            return exitCode;
        }

        output.WriteLine($"{Count(result.ScenesWritten, "cena escrita", "cenas escritas")} em {Count(result.ChaptersRead, "capítulo", "capítulos")}, modelo {settings.Model}.");
        if (result.OverviewWritten)
        {
            output.WriteLine("Sinopse e linha do tempo do jogo escritas.");
        }

        foreach (var failure in result.Failures)
        {
            output.WriteLine($"'{failure.Chapter}' não foi lido ({Count(failure.Attempts, "tentativa", "tentativas")}): {failure.Error.Message}");
        }

        if (result.BudgetStop == BudgetStop.LimitReached)
        {
            output.WriteLine("Parou no limite de gasto. Os capítulos que faltam ficam para depois: rode 'vnf bible generate --chapter' neles.");
        }

        if (result.Canceled)
        {
            output.WriteLine("Cancelado. O que já foi escrito ficou na bíblia.");
        }

        if (failed)
        {
            output.WriteLine("O que já foi escrito ficou na bíblia; rode 'vnf bible generate --chapter' para repetir o que faltou.");
        }

        WriteUsage(output, tracker.Usage, dollars);
        return exitCode;
    }

    private static Dictionary<string, object?> EstimateJson(AiRunSettings settings, BibleCostEstimate estimate, ExchangeRate rate, bool dryRun) => new()
    {
        ["dryRun"] = dryRun,
        ["model"] = settings.Model,
        ["effort"] = settings.Effort.ToString().ToLowerInvariant(),
        ["chapters"] = estimate.Chapters,
        ["scenes"] = estimate.Scenes,
        ["lines"] = estimate.Lines,
        ["estimate"] = new
        {
            inputTokens = estimate.Usage.Input,
            outputTokens = estimate.Usage.Output,
            cacheWriteTokens = estimate.Usage.CacheWrite,
            cacheReadTokens = estimate.Usage.CacheRead,
            dollars = estimate.Dollars,
            brl = estimate.Brl(rate),
            rereadChapterBrl = estimate.RereadBrl(rate),
        },
    };

    private static IReadOnlyList<BibleChapterInput> SelectChapters(IReadOnlyList<BibleChapterInput> all, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return all;
        }

        List<BibleChapterInput> picked = [];
        foreach (var wanted in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var matches = all.Where(c => Matches(c, wanted)).ToList();
            if (matches.Count == 0)
            {
                throw new UsageException(
                    $"Capítulo '{wanted}' não encontrado.",
                    $"Capítulos do projeto: {string.Join(" · ", all.Select(c => c.Number is { } n ? $"{n} {c.Name}" : c.Name))}.");
            }

            picked.AddRange(matches.Where(m => !picked.Contains(m)));
        }

        return [.. all.Where(picked.Contains)];
    }

    private static bool Matches(BibleChapterInput chapter, string wanted) =>
        (int.TryParse(wanted, out var number) && chapter.Number == number)
        || string.Equals(chapter.Name, wanted, StringComparison.OrdinalIgnoreCase)
        || chapter.Name.StartsWith(wanted + " (parte ", StringComparison.OrdinalIgnoreCase);

    private static int Show(VnfCli cli, ParseResult parseResult, string? key)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var project = Open(cli, parseResult);
        var bible = project.LoadBible();

        if (key is not null)
        {
            var scene = bible.Scene(key.Trim().ToLowerInvariant())
                ?? throw new UsageException($"A cena '{key}' não está na bíblia.", $"Rode 'vnf bible generate' ou 'vnf bible edit {key.Trim().ToLowerInvariant()}'.");
            if (json)
            {
                output.WriteLine(JsonEnvelope.Result(new { scene = SceneJson(scene) }));
            }
            else
            {
                WriteScene(output, scene);
            }

            return ErrorHandler.OkExitCode;
        }

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                synopsis = bible.Synopsis,
                synopsisEdited = bible.SynopsisEdited,
                timeline = bible.Timeline.Select(t => new { title = t.Title, note = t.Note }),
                generation = bible.Generation is { } g ? new { at = g.At, model = g.Model, scenesRead = g.ScenesRead, chapters = g.Chapters, dollars = g.Dollars } : null,
                scenes = bible.Scenes.Select(SceneJson),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (bible.IsEmpty)
        {
            output.WriteLine("A bíblia ainda está vazia. Rode 'vnf bible generate' para a IA escrevê-la.");
            return ErrorHandler.OkExitCode;
        }

        if (bible.Synopsis.Length > 0)
        {
            output.WriteLine($"Sinopse{(bible.SynopsisEdited ? " (sua)" : string.Empty)}: {bible.Synopsis}");
        }

        var table = CliViews.NewTable(CliViews.Column("Cena"), CliViews.Column("Nome"), CliViews.Column("Resumo"));
        foreach (var scene in bible.Scenes)
        {
            table.AddRow(
                new Markup(CliViews.Escape(scene.Key)),
                new Markup(CliViews.Escape(scene.Name + (scene.IsEdited(BibleField.Name) ? " (sua)" : string.Empty))),
                new Markup(scene.Summary.Length == 0 ? "[dim]sem resumo[/]" : CliViews.Escape(Shorten(scene.Summary, SummaryColumnWidth))));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteScene(TextWriter output, BibleScene scene)
    {
        string Mark(BibleField field) => scene.IsEdited(field) ? " (sua)" : string.Empty;
        output.WriteLine($"{scene.Key}{(scene.Name.Length > 0 ? $" · {scene.Name}" : string.Empty)}{Mark(BibleField.Name)}");
        output.WriteLine($"Resumo{Mark(BibleField.Summary)}: {(scene.Summary.Length > 0 ? scene.Summary : "sem resumo")}");
        output.WriteLine($"Quem aparece{Mark(BibleField.Characters)}: {Join(scene.Characters)}");
        output.WriteLine($"Tom{Mark(BibleField.Tone)}: {Join(scene.Tone)}");
        output.WriteLine($"Piadas que voltam{Mark(BibleField.Jokes)}: {Join(scene.Jokes)}");
        output.WriteLine($"Relações{Mark(BibleField.Relations)}: {(scene.Relations.Count == 0 ? "nenhuma" : string.Join("; ", scene.Relations.Select(r => $"{r.Between}: {r.Note}")))}");
    }

    private static string Join(IReadOnlyList<string> items) => items.Count == 0 ? "nenhum" : string.Join("; ", items);

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static object SceneJson(BibleScene scene) => new
    {
        key = scene.Key,
        name = scene.Name,
        summary = scene.Summary,
        characters = scene.Characters,
        tone = scene.Tone,
        jokes = scene.Jokes,
        relations = scene.Relations.Select(r => new { between = r.Between, note = r.Note }),
        edited = scene.Edited.Select(f => FieldName(f)),
    };

    private static string FieldName(BibleField field) => char.ToLowerInvariant(field.ToString()[0]) + field.ToString()[1..];

    private sealed record EditRequest(
        string? Scene, bool Game, string? Synopsis, string? Name, string? Summary, string? Characters, string? Tone, string? Jokes, string[] Relations);

    private static int Edit(VnfCli cli, ParseResult parseResult, EditRequest request)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var project = Open(cli, parseResult);

        if (request.Game)
        {
            if (request.Synopsis is null)
            {
                throw new UsageException("Com --game, diga o que mudar: --synopsis.");
            }

            project.UpdateBible(b => b.EditSynopsis(request.Synopsis));
            Report(output, json, "game", null, ["synopsis"], "Sinopse do jogo atualizada.");
            return ErrorHandler.OkExitCode;
        }

        var key = request.Scene?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
        {
            throw new UsageException("Diga a cena (yst00013) ou use --game para a sinopse do jogo.");
        }

        if (request.Synopsis is not null)
        {
            throw new UsageException("--synopsis é da sinopse do jogo: use com --game, sem cena.");
        }

        var fields = ChangedFields(request);
        if (fields.Count == 0)
        {
            throw new UsageException("Diga o que mudar: --name, --summary, --characters, --tone, --jokes ou --relation.");
        }

        var relations = request.Relations.Select(ParseRelation).ToList();
        if (project.LoadBible().Scene(key) is null && !SceneExists(project, key))
        {
            throw new UsageException($"A cena '{key}' não existe no projeto.", "Veja as chaves em 'vnf scripts list'.");
        }

        project.UpdateBible(bible =>
        {
            var scene = bible.Scene(key) ?? new BibleScene(key);
            if (request.Name is not null)
            {
                scene = scene.EditName(request.Name);
            }

            if (request.Summary is not null)
            {
                scene = scene.EditSummary(request.Summary);
            }

            if (request.Characters is not null)
            {
                scene = scene.EditCharacters(SplitList(request.Characters));
            }

            if (request.Tone is not null)
            {
                scene = scene.EditTone(SplitList(request.Tone));
            }

            if (request.Jokes is not null)
            {
                scene = scene.EditJokes(SplitList(request.Jokes));
            }

            return request.Relations.Length > 0 ? bible.WithScene(scene.EditRelations(relations)) : bible.WithScene(scene);
        });
        Report(output, json, "scene", key, fields, $"Cena {key} atualizada: {string.Join(", ", fields.Select(FieldLabel))}.");
        return ErrorHandler.OkExitCode;
    }

    private static List<string> ChangedFields(EditRequest request)
    {
        List<string> fields = [];
        if (request.Name is not null)
        {
            fields.Add("name");
        }

        if (request.Summary is not null)
        {
            fields.Add("summary");
        }

        if (request.Characters is not null)
        {
            fields.Add("characters");
        }

        if (request.Tone is not null)
        {
            fields.Add("tone");
        }

        if (request.Jokes is not null)
        {
            fields.Add("jokes");
        }

        if (request.Relations.Length > 0)
        {
            fields.Add("relations");
        }

        return fields;
    }

    private static string FieldLabel(string field) => field switch
    {
        "name" => "nome",
        "summary" => "resumo",
        "characters" => "quem aparece",
        "tone" => "tom",
        "jokes" => "piadas",
        "relations" => "relações",
        _ => field,
    };

    private static void Report(TextWriter output, bool json, string target, string? scene, IReadOnlyList<string> fields, string text)
    {
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new { action = "updated", target, scene, fields }));
        }
        else
        {
            output.WriteLine(text);
        }
    }

    private static bool SceneExists(ProjectFolder project, string key)
    {
        try
        {
            return File.Exists(project.TextPath(key));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static BibleRelation ParseRelation(string text)
    {
        var at = text.IndexOf('=', StringComparison.Ordinal);
        return at <= 0 || at == text.Length - 1 || text[..at].Trim().Length == 0 || text[(at + 1)..].Trim().Length == 0
            ? throw new UsageException($"Relação '{text}' fora do formato. Use \"Kenta e Ren=Amigos de infância\".")
            : new BibleRelation(text[..at].Trim(), text[(at + 1)..].Trim());
    }

    private static IEnumerable<string> SplitList(string text) =>
        text.Split(text.Contains(';', StringComparison.Ordinal) ? ';' : ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int Export(VnfCli cli, ParseResult parseResult, string? path)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var project = Open(cli, parseResult);
        var bible = project.LoadBible();
        var text = BibleMarkdown.Render(bible, project.LoadConfig().Name);

        if (path is null)
        {
            if (json)
            {
                output.WriteLine(JsonEnvelope.Result(new { scenes = bible.Scenes.Count, markdown = text }));
            }
            else
            {
                output.Write(text);
            }

            return ErrorHandler.OkExitCode;
        }

        var full = Path.GetFullPath(path, cli.CurrentDirectory);
        if (File.Exists(full) && !ConfirmationPrompt.Confirm(parseResult, cli, $"O arquivo {full} já existe. Sobrescrever?"))
        {
            output.WriteLine("Nada foi gravado.");
            return ErrorHandler.ErrorExitCode;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        output.WriteLine(json ? JsonEnvelope.Result(new { path = full, scenes = bible.Scenes.Count }) : $"Bíblia exportada em {full}.");
        return ErrorHandler.OkExitCode;
    }

    private static int Undo(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = Open(cli, parseResult);
        if (!project.RestorePreviousBible())
        {
            throw new UsageException("Nada a desfazer: não há bíblia anterior a que voltar.", "A bíblia anterior é guardada a cada 'vnf bible generate'.");
        }

        var scenes = project.LoadBible().Scenes.Count;
        output.WriteLine(parseResult.GetValue(cli.Options.Json)
            ? JsonEnvelope.Result(new { action = "restored", scenes })
            : $"Bíblia de antes da última geração restaurada ({Count(scenes, "cena", "cenas")}). Rodar 'vnf bible undo' de novo desfaz a volta.");
        return ErrorHandler.OkExitCode;
    }

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));

    /// <summary>O andamento em linhas no erro padrão, na ordem em que acontece (o <see cref="Progress{T}"/> publicaria fora de ordem).</summary>
    private sealed class LineProgress(TextWriter error) : IProgress<BibleProgress>
    {
        public void Report(BibleProgress value) =>
            error.WriteLine($"Capítulo {value.ChaptersDone}/{value.ChaptersTotal} · {value.Chapter} · {Number(value.ScenesDone)}/{Number(value.ScenesTotal)} cenas");
    }
}
