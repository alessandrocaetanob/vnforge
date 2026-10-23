using System.CommandLine;
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
    private const int ProgressBarWidth = 20;
    private const int PreviousLinesWindow = 30;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Option<string?> FilesOption = new("--files")
    {
        Description = "Só alguns scripts, por glob sobre a chave (\"yst001*\"); aceita vários, separados por vírgula.",
        HelpName = "padrão",
    };

    private static readonly Option<string?> StatusOption = new("--status")
    {
        Description = "Quais linhas: untranslated (padrão), machine ou all (sem tradução e máquina; nunca as traduzidas, revisadas ou aprovadas). Aceita lista separada por vírgula.",
        HelpName = "lista",
    };

    private static readonly Option<string?> ModelOption = new("--model")
    {
        Description = $"Modelo Claude: {string.Join(", ", ClaudeModels.All)}. Padrão: o de 'vnf config ai' ou o do vnforge.json.",
        HelpName = "id",
    };

    private static readonly Option<string?> EffortOption = new("--effort")
    {
        Description = "Esforço do modelo: low, medium ou high. Padrão: o do vnforge.json.",
        HelpName = "nível",
    };

    private static readonly Option<int?> BatchSizeOption = new("--batch-size")
    {
        Description = $"Linhas por chamada, de {TranslationRunOptions.MinBatchSize} a {TranslationRunOptions.MaxBatchSize}. Padrão: o do vnforge.json (30).",
        HelpName = "n",
    };

    private static readonly Option<int?> ConcurrencyOption = new("--concurrency")
    {
        Description = "Chamadas em paralelo (padrão 2).",
        HelpName = "n",
    };

    private static readonly Option<bool> DryRunOption = new("--dry-run")
    {
        Description = "Só estima tokens e custo; não chama a API nem grava nada.",
    };

    private static readonly Option<int?> LimitOption = new("--limit")
    {
        Description = "Para depois de n linhas (bom para testar o prompt).",
        HelpName = "n",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("translate", "Traduz com IA as linhas pendentes de text/*.json e grava o resultado como 'machine'.");
        command.Options.Add(FilesOption);
        command.Options.Add(StatusOption);
        command.Options.Add(ModelOption);
        command.Options.Add(EffortOption);
        command.Options.Add(BatchSizeOption);
        command.Options.Add(ConcurrencyOption);
        command.Options.Add(DryRunOption);
        command.Options.Add(LimitOption);
        command.SetAction((parseResult, token) => RunAsync(cli, parseResult, token));
        return command;
    }

    private static async Task<int> RunAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);

        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var config = project.LoadConfig();
        var settings = ReadSettings(cli, parseResult, config.Ai);
        var statuses = ParseStatuses(parseResult.GetValue(StatusOption));
        var filter = ParseFiles(parseResult.GetValue(FilesOption));
        var limit = parseResult.GetValue(LimitOption);
        if (limit < 0)
        {
            throw new UsageException("--limit precisa ser zero ou maior.");
        }

        var texts = project.LoadAllText();
        var files = new Dictionary<string, List<TextUnit>>(StringComparer.Ordinal);
        var keyById = new Dictionary<string, string>(StringComparer.Ordinal);
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = new List<TextUnit>();
        foreach (var (key, units) in texts)
        {
            if (filter is not null && !filter.IsMatch(key))
            {
                continue;
            }

            var selected = units.Where(u => statuses.Contains(u.Status) && !string.IsNullOrWhiteSpace(u.Source)).ToList();
            if (selected.Count == 0 || (limit is { } max && pending.Count >= max))
            {
                continue;
            }

            files[key] = [.. units];
            for (var i = 0; i < units.Count; i++)
            {
                keyById[units[i].Id] = key;
                indexById[units[i].Id] = i;
            }

            pending.AddRange(limit is { } cap ? selected.Take(cap - pending.Count) : selected);
        }

        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, StyleGuideFileName)));
        var cachePath = Path.Combine(project.CacheDirectory, CacheFileName);
        var cache = TranslationCache.Load(cachePath);

        if (parseResult.GetValue(DryRunOption))
        {
            return WriteDryRun(output, json, settings, files.Count, pending, builder, cache);
        }

        if (pending.Count == 0)
        {
            return WriteResult(output, json, settings, pending.Count, new RunSummary());
        }

        var provider = cli.TranslationProviderFactory is { } factory
            ? factory(settings.Model, settings.Effort)
            : CreateClaudeProvider(cli, settings);
        var ledger = new UsageLedger();
        var chain = new CachingTranslationProvider(new ValidatingTranslationProvider(provider), cache, settings.Model, ledger);
        var runner = new TranslationRunner(chain, new TranslationRunOptions { BatchSize = settings.BatchSize, MaxConcurrency = settings.Concurrency });

        var gate = new object();
        var summary = new RunSummary();

        TranslationBatch BuildBatch(IReadOnlyList<TextUnit> lines)
        {
            lock (gate)
            {
                var first = lines[0].Id;
                var previous = files[keyById[first]].Take(indexById[first]).TakeLast(PreviousLinesWindow).ToList();
                return builder.Build(lines, previous);
            }
        }

        void OnBatchTranslated(IReadOnlyList<TranslationResult> results)
        {
            lock (gate)
            {
                var origin = new TranslationOrigin(ProviderName, settings.Model, DateTimeOffset.UtcNow);
                var touched = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var result in results)
                {
                    if (result.Refused || string.IsNullOrWhiteSpace(result.Translation))
                    {
                        summary.Refused.Add(result.Id);
                        continue;
                    }

                    var key = keyById[result.Id];
                    var list = files[key];
                    var index = indexById[result.Id];
                    list[index] = list[index] with
                    {
                        Translation = result.Translation,
                        Status = TranslationStatus.Machine,
                        Origin = origin,
                        PreviousSource = null,
                    };
                    touched.Add(key);
                    summary.Translated++;
                }

                foreach (var key in touched)
                {
                    project.SaveText(key, files[key]);
                }

                summary.Handled += results.Count;
                if (!json)
                {
                    error.WriteLine(ProgressLine(summary.Handled, pending.Count, touched.LastOrDefault() ?? keyById[results[0].Id]));
                }
            }
        }

        TranslationRunResult run;
        try
        {
            run = await runner.RunAsync(pending, BuildBatch, OnBatchTranslated, null, token).ConfigureAwait(false);
        }
        finally
        {
            if (cache.Count > 0)
            {
                cache.Save(cachePath);
            }
        }

        summary.Cached = ledger.CacheHits;
        summary.Failed = [.. run.Failures];
        summary.Usage = (provider as ClaudeTranslationProvider)?.Usage;

        if (run.CallbackError is { } callbackError)
        {
            throw new VNForgeException($"Não foi possível gravar a tradução em text/: {callbackError.Message}", callbackError);
        }

        if (run.Canceled)
        {
            if (!json)
            {
                error.WriteLine($"Cancelado: {Count(summary.Translated, "linha gravada", "linhas gravadas")} antes de parar.");
            }

            token.ThrowIfCancellationRequested();
        }

        return WriteResult(output, json, settings, pending.Count, summary);
    }

    private static int WriteDryRun(
        TextWriter output,
        bool json,
        TranslateSettings settings,
        int fileCount,
        List<TextUnit> pending,
        TranslationBatchBuilder builder,
        TranslationCache cache)
    {
        var batches = pending.Chunk(settings.BatchSize).Select(chunk => builder.Build(chunk));
        var estimate = CostEstimator.EstimateBatches(batches, settings.Model, cache);

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                dryRun = true,
                model = settings.Model,
                effort = settings.Effort.ToString().ToLowerInvariant(),
                batchSize = settings.BatchSize,
                concurrency = settings.Concurrency,
                files = fileCount,
                lines = pending.Count,
                estimate = new
                {
                    lines = estimate.Lines,
                    cachedLines = estimate.CachedLines,
                    inputTokens = estimate.Usage.Input,
                    outputTokens = estimate.Usage.Output,
                    dollars = estimate.Dollars,
                },
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Estimativa ({settings.Model}): {Count(estimate.Lines, "linha", "linhas")} para a API, {Number(estimate.CachedLines)} já no cache.");
        output.WriteLine($"~{Tokens(estimate.Usage.Input)} de entrada, ~{Tokens(estimate.Usage.Output)} de saída; custo estimado {Dollars(estimate.Dollars)}.");
        output.WriteLine("É uma ordem de grandeza: o custo real aparece ao fim de 'vnf translate'.");
        return ErrorHandler.OkExitCode;
    }

    private static int WriteResult(TextWriter output, bool json, TranslateSettings settings, int selected, RunSummary summary)
    {
        var failedLines = summary.Failed.Sum(f => f.LineIds.Count);
        var exitCode = summary.Failed.Count > 0 ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        var dollars = summary.Usage is { } u
            ? ModelPricing.For(settings.Model).Cost(new TokenUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens))
            : (decimal?)null;

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(
                new
                {
                    dryRun = false,
                    model = settings.Model,
                    effort = settings.Effort.ToString().ToLowerInvariant(),
                    batchSize = settings.BatchSize,
                    concurrency = settings.Concurrency,
                    lines = selected,
                    translated = summary.Translated,
                    cached = summary.Cached,
                    refused = summary.Refused.Order(StringComparer.Ordinal),
                    failed = summary.Failed.Select(f => new { lines = f.LineIds, error = f.Error.Message, attempts = f.Attempts }),
                    usage = summary.Usage is { } usage
                        ? new { input = usage.InputTokens, output = usage.OutputTokens, cacheRead = usage.CacheReadInputTokens, cacheWrite = usage.CacheCreationInputTokens }
                        : null,
                    dollars,
                },
                exitCode));
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

        if (summary.Refused.Count > 0)
        {
            output.WriteLine($"{Count(summary.Refused.Count, "linha recusada", "linhas recusadas")} pelo modelo ou fora das regras de validação; seguem sem tradução: {string.Join(", ", summary.Refused.Order(StringComparer.Ordinal).Take(10))}{(summary.Refused.Count > 10 ? ", ..." : string.Empty)}");
        }

        foreach (var failure in summary.Failed)
        {
            output.WriteLine($"Lote com falha ({Count(failure.LineIds.Count, "linha", "linhas")}, {Count(failure.Attempts, "tentativa", "tentativas")}): {failure.Error.Message}");
        }

        if (failedLines > 0)
        {
            output.WriteLine($"{Count(failedLines, "linha segue", "linhas seguem")} sem tradução; rode 'vnf translate' de novo para repetir só o que falta.");
        }

        if (summary.Usage is { } usageTotals)
        {
            output.WriteLine($"Tokens: {Tokens(usageTotals.InputTokens)} de entrada, {Tokens(usageTotals.OutputTokens)} de saída, {Tokens(usageTotals.CacheReadInputTokens)} lidos do cache de prompt; custo {Dollars(dollars ?? 0m)}.");
        }

        return exitCode;
    }

    private static ITranslationProvider CreateClaudeProvider(VnfCli cli, TranslateSettings settings)
    {
        var key = ApiKeyResolver.Resolve(cli.ApiKeyStore).Key
            ?? throw new UsageException(
                $"Sem chave de API. Defina {ApiKeyResolver.EnvironmentVariable} ou rode 'vnf config ai --set-key'. Use --dry-run para só estimar o custo.");
        return ClaudeProviderFactory.Create(key, new ClaudeOptions { Model = settings.Model, Effort = settings.Effort });
    }

    private static TranslateSettings ReadSettings(VnfCli cli, ParseResult parseResult, AiConfig ai)
    {
        var settingsFile = new AiUserSettingsFile(cli.UserConfigDirectory);
        var model = parseResult.GetValue(ModelOption)
            ?? (File.Exists(settingsFile.Path) ? settingsFile.Load().Model : null)
            ?? (ClaudeModels.All.Contains(ai.Model) ? ai.Model : ClaudeModels.Opus);
        if (!ClaudeModels.All.Contains(model))
        {
            throw new UsageException($"Modelo desconhecido: '{model}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
        }

        var effortText = parseResult.GetValue(EffortOption);
        var effort = ParseEffort(effortText ?? ai.Effort, strict: effortText is not null);

        var requestedBatchSize = parseResult.GetValue(BatchSizeOption);
        var batchSize = requestedBatchSize ?? ai.BatchSize;
        if (batchSize < TranslationRunOptions.MinBatchSize || batchSize > TranslationRunOptions.MaxBatchSize)
        {
            var origin = requestedBatchSize is null ? "ai.batchSize do vnforge.json" : "--batch-size";
            throw new UsageException($"{origin} precisa ficar entre {TranslationRunOptions.MinBatchSize} e {TranslationRunOptions.MaxBatchSize} (recebeu {batchSize}).");
        }

        var concurrency = parseResult.GetValue(ConcurrencyOption) ?? 2;
        if (concurrency < 1)
        {
            throw new UsageException("--concurrency precisa ser 1 ou maior.");
        }

        return new TranslateSettings(model, effort, batchSize, concurrency);
    }

    private static ClaudeEffort ParseEffort(string text, bool strict)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "low": return ClaudeEffort.Low;
            case "medium": return ClaudeEffort.Medium;
            case "high": return ClaudeEffort.High;
            default:
                return strict
                    ? throw new UsageException($"Esforço desconhecido: '{text}'. Use low, medium ou high.")
                    : ClaudeEffort.Medium;
        }
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

    /// <summary>Glob sobre a chave do script (<c>*</c> e <c>?</c>), sem diferenciar maiúsculas; <c>.json</c> no fim do padrão é ignorado.</summary>
    private static Regex? ParseFiles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var alternatives = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? p[..^5] : p)
            .Select(p => Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal));
        return new Regex(
            $"^(?:{string.Join('|', alternatives)})$",
            RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static string ProgressLine(int done, int total, string key)
    {
        var fraction = total == 0 ? 1.0 : Math.Min(1.0, (double)done / total);
        var filled = (int)Math.Floor(fraction * ProgressBarWidth);
        var bar = new string('#', filled) + new string('-', ProgressBarWidth - filled);
        return $"[{bar}] {(int)Math.Floor(fraction * 100)}%  {Number(done)}/{Number(total)}  {key}.json";
    }

    private static string Count(int value, string singular, string plural) => $"{Number(value)} {(value == 1 ? singular : plural)}";

    private static string Number(long value) => value.ToString("N0", PortugueseBrazil);

    private static string Tokens(long value) => value >= 1_000_000
        ? string.Create(PortugueseBrazil, $"{value / 1_000_000.0:0.0} M tokens")
        : $"{Number(value)} tokens";

    private static string Dollars(decimal value) => string.Create(PortugueseBrazil, $"US$ {value:0.00}");

    private sealed record TranslateSettings(string Model, ClaudeEffort Effort, int BatchSize, int Concurrency);

    private sealed class RunSummary
    {
        public int Translated { get; set; }

        public int Handled { get; set; }

        public int Cached { get; set; }

        public List<string> Refused { get; } = [];

        public IReadOnlyList<TranslationBatchFailure> Failed { get; set; } = [];

        public ClaudeUsage? Usage { get; set; }
    }
}
