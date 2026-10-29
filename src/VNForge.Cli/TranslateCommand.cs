using System.CommandLine;
using Spectre.Console;
using System.Globalization;
using System.Text.RegularExpressions;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using static VNForge.Cli.AiCommandSupport;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf translate</c>: traduz com IA as linhas pendentes de <c>text/*.json</c>, em lotes, e grava cada lote assim que volta
/// (<c>status: machine</c> e <c>origin</c>), para uma queda ou um cancelamento não perder o que já foi pago.
/// </summary>
internal static class TranslateCommand
{
    private const string ProviderName = "anthropic";
    private const string StyleGuideFileName = "style.md";
    private const string CacheFileName = "translations.json";

    private static readonly AiOptions Common = new();

    private static readonly Option<string?> StatusOption = new("--status")
    {
        Description = "Quais linhas: untranslated (padrão), machine ou all (sem tradução e máquina; nunca as traduzidas, revisadas ou aprovadas). Aceita lista separada por vírgula.",
        HelpName = "lista",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("translate", "Traduz com IA as linhas pendentes de text/*.json e grava o resultado como 'machine'.");
        Common.AddTo(command, StatusOption);
        command.SetAction((parseResult, token) => RunAsync(cli, parseResult, token));
        return command;
    }

    private static async Task<int> RunAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var (output, error, json, project, settings, filter, limit) = AiCommandContext.Open(cli, parseResult, Common);
        var statuses = ParseStatuses(parseResult.GetValue(StatusOption));

        var selection = LineSelection.Build(project.LoadAllText(), ScriptFilter(filter), limit, u => statuses.Contains(u.Status) && !string.IsNullOrWhiteSpace(u.Source));
        var pending = selection.Pending;

        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, StyleGuideFileName)),
            style: project.LoadConfig().Style,
            addressTerms: project.LoadAddressTerms());
        var cachePath = Path.Combine(project.CacheDirectory, CacheFileName);
        var cache = TranslationCache.Load(cachePath);

        if (parseResult.GetValue(Common.DryRun))
        {
            return WriteDryRun(output, json, settings, selection, builder, cache);
        }

        if (pending.Count == 0)
        {
            return WriteResult(output, json, settings, pending.Count, new RunSummary());
        }

        var budget = ReadBudget(parseResult, cli, error);
        var provider = cli.TranslationProviderFactory is { } factory
            ? factory(settings.Model, settings.Effort)
            : CreateClaudeProvider(cli, settings);
        ClaudeUsage? UsageOf() => (provider as ClaudeTranslationProvider)?.Usage;
        var ledger = new UsageLedger();
        var chain = KeyboardPunctuationProvider.Wrap(new CachingTranslationProvider(new ValidatingTranslationProvider(provider), cache, settings.Model, ledger), project.LoadConfig());
        var tracker = new RunCostTracker(project, settings.Model, budget.Rate, UsageOf);
        var estimator = new RequestEstimator(selection, builder, settings.Model, settings.Effort, settings.BatchSize, budget.Rate, cache);
        var summary = new RunSummary { Budget = budget };

        // A pergunta do valor de aviso vem antes da barra de progresso: dentro dela o Spectre redesenharia a tela por cima da resposta.
        var asker = tracker.CreateGuard(budget.Limits, estimator.Brl, (question, _) => Task.FromResult(ConfirmationPrompt.Confirm(parseResult, cli, AskText(question))));
        if (await asker.AuthorizeAsync(asker.EstimateRequest(pending), token).ConfigureAwait(false) is { } refusal)
        {
            summary.Stop = refusal;
            return WriteResult(output, json, settings, pending.Count, summary);
        }

        var guard = tracker.CreateGuard(new BudgetLimits(budget.Limits.Limit, null), estimator.Brl, null);
        var runner = new TranslationRunner(chain, new TranslationRunOptions { BatchSize = settings.BatchSize, MaxConcurrency = settings.Concurrency, Budget = guard });

        var gate = new object();
        var reporter = new BatchReporter(settings.Model, pending.Count, settings.BatchSize, UsageOf);

        void OnBatchTranslated(IReadOnlyList<TranslationResult> results)
        {
            lock (gate)
            {
                var origin = new TranslationOrigin(ProviderName, settings.Model, DateTimeOffset.UtcNow);
                var saved = selection.ApplyAndSave(project, results, (result, unit) => Apply(summary, origin, result, unit));
                summary.Skipped += saved.Skipped;
                summary.Handled += results.Count;
                if (reporter.IsLive)
                {
                    reporter.BatchDone(summary.Handled);
                }
                else if (!json)
                {
                    error.WriteLine(ProgressLine(summary.Handled, pending.Count, saved.LastKey));
                }
            }
        }

        if (cli.CanAnimate)
        {
            cli.Ansi.MarkupLine($"Traduzindo com [{CliPalette.Hex(CliPalette.Accent)}]{CliViews.Escape(settings.Model)}[/] · esforço {settings.Effort.ToString().ToLowerInvariant()}");
        }

        TranslationRunResult run;
        try
        {
            run = await reporter.RunAsync(
                cli.Ansi,
                cli.CanAnimate,
                "Traduzindo",
                () => runner.RunAsync(pending, lines => selection.BuildBatch(builder, lines), OnBatchTranslated, null, token)).ConfigureAwait(false);
        }
        finally
        {
            if (cache.Count > 0)
            {
                cache.Save(cachePath);
            }

            tracker.Record();
        }

        summary.Cached = ledger.CacheHits;
        summary.Stop = run.BudgetStop;
        summary.Failed = [.. run.Failures];
        summary.Usage = tracker.Usage;

        if (run.CallbackError is { } callbackError)
        {
            throw new VNForgeException($"Não foi possível gravar a tradução em text/: {callbackError.Message}", callbackError);
        }

        if (run.Canceled)
        {
            if (!json)
            {
                await error.WriteLineAsync($"Cancelado: {Count(summary.Translated, "linha gravada", "linhas gravadas")} antes de parar.");
            }

            token.ThrowIfCancellationRequested();
        }

        return WriteResult(output, json, settings, pending.Count, summary);
    }

    /// <summary>Grava a tradução na linha como <c>machine</c>; devolve <c>null</c> para a linha recusada ou vazia, que fica como estava.</summary>
    private static TextUnit? Apply(RunSummary summary, TranslationOrigin origin, TranslationResult result, TextUnit unit)
    {
        if (result.Refused || string.IsNullOrWhiteSpace(result.Translation))
        {
            summary.Refused.Add(result.Id);
            return null;
        }

        summary.Translated++;
        return unit with
        {
            Translation = result.Translation,
            Status = TranslationStatus.Machine,
            Origin = origin,
            PreviousSource = null,
        };
    }

    private static int WriteDryRun(
        TextWriter output,
        bool json,
        AiRunSettings settings,
        LineSelection selection,
        TranslationBatchBuilder builder,
        TranslationCache cache)
    {
        var batches = selection.Pending.Chunk(settings.BatchSize).Select(chunk => selection.BuildBatch(builder, chunk));
        var estimate = CostEstimator.EstimateBatches(batches, settings.Model, cache, settings.Effort);

        if (json)
        {
            var data = JsonBase(settings, dryRun: true, selection.Pending.Count);
            data["files"] = selection.FileCount;
            data["estimate"] = new
            {
                lines = estimate.Lines,
                cachedLines = estimate.CachedLines,
                inputTokens = estimate.Usage.Input,
                outputTokens = estimate.Usage.Output,
                dollars = estimate.Dollars,
            };
            output.WriteLine(JsonEnvelope.Result(data));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Estimativa ({settings.Model}): {Count(estimate.Lines, "linha", "linhas")} para a API, {Number(estimate.CachedLines)} já no cache.");
        WriteEstimateTail(output, estimate.Usage, estimate.Dollars, "vnf translate");
        return ErrorHandler.OkExitCode;
    }

    private static int WriteResult(TextWriter output, bool json, AiRunSettings settings, int selected, RunSummary summary)
    {
        var exitCode = summary.Failed.Count > 0 || summary.Stop is not null ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        var dollars = RunCostTracker.DollarsFor(settings.Model, summary.Usage);

        if (json)
        {
            var data = JsonBase(settings, dryRun: false, selected);
            data["translated"] = summary.Translated;
            data["cached"] = summary.Cached;
            data["skipped"] = summary.Skipped;
            data["budgetStop"] = summary.Stop switch
            {
                BudgetStop.LimitReached => "limitReached",
                BudgetStop.Declined => "declined",
                _ => null,
            };
            output.WriteLine(JsonEnvelope.Result(WithOutcome(data, summary.Refused, summary.Failed, summary.Usage, dollars), exitCode));
            return exitCode;
        }

        if (selected == 0)
        {
            output.WriteLine("Nada a traduzir: nenhuma linha com os status e arquivos pedidos.");
            return exitCode;
        }

        output.WriteLine($"{Count(summary.Translated, "linha traduzida", "linhas traduzidas")} de {Number(selected)} (status: machine), modelo {settings.Model}.");
        if (summary.Cached > 0)
        {
            output.WriteLine($"{Count(summary.Cached, "linha veio", "linhas vieram")} do cache, sem custo.");
        }

        if (summary.Skipped > 0)
        {
            output.WriteLine($"{Count(summary.Skipped, "linha foi mudada", "linhas foram mudadas")} durante a execução e ficou como a deixaram; a tradução da IA não a sobrescreveu.");
        }

        WriteBudgetStop(output, summary);
        WriteOutcome(
            output,
            summary.Refused,
            "pelo modelo ou fora das regras de validação; seguem sem tradução",
            summary.Failed,
            "sem tradução; rode 'vnf translate' de novo para repetir só o que falta.",
            summary.Usage,
            dollars);

        return exitCode;
    }

    /// <summary>O orçamento do <c>app.json</c>. Arquivo ilegível avisa no erro padrão e só segue com <c>--yes</c>: ele pode esconder um limite que a pessoa pôs.</summary>
    private static BudgetSettings ReadBudget(ParseResult parseResult, VnfCli cli, TextWriter error)
    {
        var reading = AppBudgetFile.Load(cli.UserConfigDirectory);
        if (reading.Problem is not { } problem)
        {
            return reading.Settings;
        }

        error.WriteLine($"Aviso: {problem} O limite de gasto e o valor de aviso do app não puderam ser conferidos.");
        return parseResult.GetValue(cli.Options.Yes)
            ? reading.Settings
            : throw new UsageException("Corrija ou apague o app.json, ou use --yes para traduzir sem conferir o limite de gasto.");
    }

    private static string AskText(BudgetQuestion question) =>
        $"Este pedido custa cerca de {Brl(question.EstimatedCost)}, acima do valor de aviso de {Brl(question.WarnAbove)}. Continuar?";

    private static void WriteBudgetStop(TextWriter output, RunSummary summary)
    {
        switch (summary.Stop)
        {
            case BudgetStop.LimitReached:
                var limit = summary.Budget?.Limits.Limit;
                output.WriteLine($"Parou no limite de gasto{(limit is { } cap ? $" de {Brl(cap)}" : string.Empty)}. As linhas que faltam seguem sem tradução; aumente o limite nos Ajustes do app para continuar.");
                break;
            case BudgetStop.Declined:
                output.WriteLine("O pedido não foi confirmado: nenhuma linha foi enviada à IA.");
                break;
        }
    }

    private static ClaudeTranslationProvider CreateClaudeProvider(VnfCli cli, AiRunSettings settings)
    {
        var key = RequireApiKey(cli);
        return ClaudeProviderFactory.Create(key, new ClaudeOptions { Model = settings.Model, Effort = settings.Effort });
    }

    private static HashSet<TranslationStatus> ParseStatuses(string? text)
    {
        var result = new HashSet<TranslationStatus>();
        foreach (var part in (text ?? "untranslated").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "untranslated":
                    result.Add(TranslationStatus.Untranslated);
                    break;
                case "machine":
                    result.Add(TranslationStatus.Machine);
                    break;
                case "all":
                    result.Add(TranslationStatus.Untranslated);
                    result.Add(TranslationStatus.Machine);
                    break;
                default:
                    throw new UsageException($"Status desconhecido em --status: '{part}'. Use untranslated, machine ou all.");
            }
        }

        return result.Count > 0 ? result : throw new UsageException("--status veio vazio. Use untranslated, machine ou all.");
    }

    private sealed class RunSummary
    {
        public int Translated { get; set; }

        public int Handled { get; set; }

        public int Skipped { get; set; }

        public int Cached { get; set; }

        public List<string> Refused { get; } = [];

        public IReadOnlyList<TranslationBatchFailure> Failed { get; set; } = [];

        public ClaudeUsage? Usage { get; set; }

        public BudgetStop? Stop { get; set; }

        public BudgetSettings? Budget { get; set; }
    }
}
