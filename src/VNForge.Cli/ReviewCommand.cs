using System.CommandLine;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Review;
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
/// <c>vnf review</c>: pede à IA uma revisão das linhas já traduzidas, em lotes, e grava cada sugestão em <c>notes</c> assim que o lote
/// volta. Nunca toca em <c>translation</c>, <c>status</c> nem <c>origin</c>: quem decide é o revisor.
/// </summary>
internal static class ReviewCommand
{
    /// <summary>Início da linha de <c>notes</c> que pertence à IA; uma nova revisão substitui só esta linha e preserva as notas humanas.</summary>
    internal const string NotePrefix = "[IA] ";

    private const string StyleGuideFileName = "style.md";
    private const int PreviousLinesWindow = 30;
    private const int OutputTokensPerLine = 40;

    private static readonly Option<string?> FilesOption = new("--files")
    {
        Description = "Só alguns scripts, por glob sobre a chave (\"yst001*\"); aceita vários, separados por vírgula.",
        HelpName = "padrão",
    };

    private static readonly Option<string?> StatusOption = new("--status")
    {
        Description = "Quais linhas: machine (padrão), translated, reviewed, approved ou all (todas as que têm tradução). Aceita lista separada por vírgula.",
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
        var command = new Command("review", "Pede à IA uma revisão das linhas traduzidas e grava as sugestões em 'notes', sem alterar traduções.");
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

    /// <summary>
    /// Junta a sugestão às notas: a linha <c>[IA] ...</c> anterior sai, as demais ficam, e a nova entra no fim. O resultado nunca
    /// tem quebra de linha dentro da sugestão, para a próxima revisão achar a linha da IA sem ambiguidade.
    /// </summary>
    internal static string MergeNote(string existing, string suggestion)
    {
        var kept = string.Join('\n', existing.Split('\n').Where(l => !l.StartsWith(NotePrefix, StringComparison.Ordinal))).TrimEnd('\r', '\n');
        var oneLine = string.Join(' ', suggestion.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var note = NotePrefix + oneLine;
        return kept.Length == 0 ? note : kept + '\n' + note;
    }

    private static async Task<int> RunAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);

        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var config = project.LoadConfig();
        var settings = ReadSettings(
            cli,
            config.Ai,
            parseResult.GetValue(ModelOption),
            parseResult.GetValue(EffortOption),
            parseResult.GetValue(BatchSizeOption),
            parseResult.GetValue(ConcurrencyOption));
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

            var selected = units.Where(u => statuses.Contains(u.Status) && u.HasTranslation).ToList();
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

        if (parseResult.GetValue(DryRunOption))
        {
            return WriteDryRun(output, json, settings, files.Count, pending);
        }

        if (pending.Count == 0)
        {
            return WriteResult(output, json, settings, pending.Count, new RunSummary());
        }

        var provider = cli.ReviewProviderFactory is { } factory
            ? factory(settings.Model, settings.Effort)
            : ClaudeProviderFactory.CreateReview(RequireApiKey(cli), new ClaudeOptions { Model = settings.Model, Effort = settings.Effort });
        var runner = new TranslationRunner(
            new ReviewRunnerAdapter(provider),
            new TranslationRunOptions { BatchSize = settings.BatchSize, MaxConcurrency = settings.Concurrency });

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

        void OnBatchReviewed(IReadOnlyList<TranslationResult> results)
        {
            lock (gate)
            {
                var touched = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var result in results)
                {
                    if (result.Refused)
                    {
                        summary.Refused.Add(result.Id);
                        continue;
                    }

                    summary.Reviewed++;
                    if (string.IsNullOrWhiteSpace(result.Translation))
                    {
                        summary.Clean++;
                        continue;
                    }

                    var key = keyById[result.Id];
                    var list = files[key];
                    var index = indexById[result.Id];
                    // Só Notes muda: translation, status e origin ficam como estavam.
                    list[index] = list[index] with { Notes = MergeNote(list[index].Notes, result.Translation) };
                    touched.Add(key);
                    summary.Suggestions++;
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

        var run = await runner.RunAsync(pending, BuildBatch, OnBatchReviewed, null, token).ConfigureAwait(false);

        summary.Failed = [.. run.Failures];
        summary.Usage = (provider as ClaudeReviewProvider)?.Usage;

        if (run.CallbackError is { } callbackError)
        {
            throw new VNForgeException($"Não foi possível gravar a revisão em text/: {callbackError.Message}", callbackError);
        }

        if (run.Canceled)
        {
            if (!json)
            {
                error.WriteLine($"Cancelado: {Count(summary.Suggestions, "sugestão gravada", "sugestões gravadas")} antes de parar.");
            }

            token.ThrowIfCancellationRequested();
        }

        return WriteResult(output, json, settings, pending.Count, summary);
    }

    private static int WriteDryRun(TextWriter output, bool json, AiRunSettings settings, int fileCount, List<TextUnit> pending)
    {
        // A revisão manda original e tradução; a saída é só a nota curta.
        var source = CostEstimator.Estimate(pending, settings.Model);
        var translation = CostEstimator.Estimate(pending.Select(u => u with { Source = u.Translation ?? string.Empty, Speaker = null }), settings.Model);
        var usage = new TokenUsage(source.Usage.Input + translation.Usage.Input, (long)pending.Count * OutputTokensPerLine);
        var dollars = ModelPricing.For(settings.Model).Cost(usage);

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
                    lines = pending.Count,
                    inputTokens = usage.Input,
                    outputTokens = usage.Output,
                    dollars,
                },
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Estimativa ({settings.Model}): {Count(pending.Count, "linha", "linhas")} para a API.");
        output.WriteLine($"~{Tokens(usage.Input)} de entrada, ~{Tokens(usage.Output)} de saída; custo estimado {Dollars(dollars)}.");
        output.WriteLine("É uma ordem de grandeza: o custo real aparece ao fim de 'vnf review'.");
        return ErrorHandler.OkExitCode;
    }

    private static int WriteResult(TextWriter output, bool json, AiRunSettings settings, int selected, RunSummary summary)
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
                    reviewed = summary.Reviewed,
                    suggestions = summary.Suggestions,
                    clean = summary.Clean,
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
            output.WriteLine("Nada a revisar: nenhuma linha traduzida com os status e arquivos pedidos.");
            return exitCode;
        }

        output.WriteLine($"{Count(summary.Reviewed, "linha revisada", "linhas revisadas")} de {Number(selected)}, modelo {settings.Model}: {Count(summary.Suggestions, "sugestão gravada", "sugestões gravadas")} em 'notes', {Count(summary.Clean, "linha sem ressalva", "linhas sem ressalva")}.");
        output.WriteLine("As traduções e os status não foram alterados.");

        if (summary.Refused.Count > 0)
        {
            output.WriteLine($"{Count(summary.Refused.Count, "linha recusada", "linhas recusadas")} pelo modelo; seguem sem sugestão: {string.Join(", ", summary.Refused.Order(StringComparer.Ordinal).Take(10))}{(summary.Refused.Count > 10 ? ", ..." : string.Empty)}");
        }

        foreach (var failure in summary.Failed)
        {
            output.WriteLine($"Lote com falha ({Count(failure.LineIds.Count, "linha", "linhas")}, {Count(failure.Attempts, "tentativa", "tentativas")}): {failure.Error.Message}");
        }

        if (failedLines > 0)
        {
            output.WriteLine($"{Count(failedLines, "linha segue", "linhas seguem")} sem revisão; rode 'vnf review' de novo.");
        }

        if (summary.Usage is { } usageTotals)
        {
            output.WriteLine($"Tokens: {Tokens(usageTotals.InputTokens)} de entrada, {Tokens(usageTotals.OutputTokens)} de saída, {Tokens(usageTotals.CacheReadInputTokens)} lidos do cache de prompt; custo {Dollars(dollars ?? 0m)}.");
        }

        return exitCode;
    }

    private static HashSet<TranslationStatus> ParseStatuses(string? text)
    {
        var result = new HashSet<TranslationStatus>();
        foreach (var part in (text ?? "machine").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "machine":
                    result.Add(TranslationStatus.Machine);
                    break;
                case "translated":
                    result.Add(TranslationStatus.Translated);
                    break;
                case "reviewed":
                    result.Add(TranslationStatus.Reviewed);
                    break;
                case "approved":
                    result.Add(TranslationStatus.Approved);
                    break;
                case "all":
                    result.UnionWith([TranslationStatus.Machine, TranslationStatus.Translated, TranslationStatus.Reviewed, TranslationStatus.Approved]);
                    break;
                default:
                    throw new UsageException($"Status desconhecido em --status: '{part}'. Use machine, translated, reviewed, approved ou all.");
            }
        }

        return result.Count > 0 ? result : throw new UsageException("--status veio vazio. Use machine, translated, reviewed, approved ou all.");
    }

    private sealed class RunSummary
    {
        public int Reviewed { get; set; }

        public int Suggestions { get; set; }

        public int Clean { get; set; }

        public int Handled { get; set; }

        public List<string> Refused { get; } = [];

        public IReadOnlyList<TranslationBatchFailure> Failed { get; set; } = [];

        public ClaudeUsage? Usage { get; set; }
    }
}
