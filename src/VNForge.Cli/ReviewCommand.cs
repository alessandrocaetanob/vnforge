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
    private const int OutputTokensPerLine = 40;

    private static readonly AiOptions Common = new();

    private static readonly Option<string?> StatusOption = new("--status")
    {
        Description = "Quais linhas: machine (padrão), translated, reviewed, approved ou all (todas as que têm tradução). Aceita lista separada por vírgula.",
        HelpName = "lista",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("review", "Pede à IA uma revisão das linhas traduzidas e grava as sugestões em 'notes', sem alterar traduções.");
        Common.AddTo(command, StatusOption);
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
        var (output, error, json, project, settings, filter, limit) = AiCommandContext.Open(cli, parseResult, Common);
        var statuses = ParseStatuses(parseResult.GetValue(StatusOption));

        var selection = LineSelection.Build(project.LoadAllText(), filter, limit, u => statuses.Contains(u.Status) && u.HasTranslation);
        var files = selection.Files;
        var pending = selection.Pending;

        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, StyleGuideFileName)));

        if (parseResult.GetValue(Common.DryRun))
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
                return selection.BuildBatch(builder, lines);
            }
        }

        void OnBatchReviewed(IReadOnlyList<TranslationResult> results)
        {
            lock (gate)
            {
                var lastKey = selection.ApplyAndSave(project, results, (result, unit) => Annotate(summary, result, unit));
                summary.Handled += results.Count;
                if (!json)
                {
                    error.WriteLine(ProgressLine(summary.Handled, pending.Count, lastKey));
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
                await error.WriteLineAsync($"Cancelado: {Count(summary.Suggestions, "sugestão gravada", "sugestões gravadas")} antes de parar.");
            }

            token.ThrowIfCancellationRequested();
        }

        return WriteResult(output, json, settings, pending.Count, summary);
    }

    /// <summary>Anota a sugestão na linha; devolve <c>null</c> quando não há o que gravar (recusa ou linha sem ressalva).</summary>
    private static TextUnit? Annotate(RunSummary summary, TranslationResult result, TextUnit unit)
    {
        if (result.Refused)
        {
            summary.Refused.Add(result.Id);
            return null;
        }

        summary.Reviewed++;
        if (string.IsNullOrWhiteSpace(result.Translation))
        {
            summary.Clean++;
            return null;
        }

        summary.Suggestions++;
        // Só Notes muda: translation, status e origin ficam como estavam.
        return unit with { Notes = MergeNote(unit.Notes, result.Translation) };
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
            var data = JsonBase(settings, dryRun: true, pending.Count);
            data["files"] = fileCount;
            data["estimate"] = new { lines = pending.Count, inputTokens = usage.Input, outputTokens = usage.Output, dollars };
            output.WriteLine(JsonEnvelope.Result(data));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Estimativa ({settings.Model}): {Count(pending.Count, "linha", "linhas")} para a API.");
        WriteEstimateTail(output, usage, dollars, "vnf review");
        return ErrorHandler.OkExitCode;
    }

    private static int WriteResult(TextWriter output, bool json, AiRunSettings settings, int selected, RunSummary summary)
    {
        var exitCode = summary.Failed.Count > 0 ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        var dollars = DollarsFor(settings.Model, summary.Usage);

        if (json)
        {
            var data = JsonBase(settings, dryRun: false, selected);
            data["reviewed"] = summary.Reviewed;
            data["suggestions"] = summary.Suggestions;
            data["clean"] = summary.Clean;
            output.WriteLine(JsonEnvelope.Result(WithOutcome(data, summary.Refused, summary.Failed, summary.Usage, dollars), exitCode));
            return exitCode;
        }

        if (selected == 0)
        {
            output.WriteLine("Nada a revisar: nenhuma linha traduzida com os status e arquivos pedidos.");
            return exitCode;
        }

        output.WriteLine($"{Count(summary.Reviewed, "linha revisada", "linhas revisadas")} de {Number(selected)}, modelo {settings.Model}: {Count(summary.Suggestions, "sugestão gravada", "sugestões gravadas")} em 'notes', {Count(summary.Clean, "linha sem ressalva", "linhas sem ressalva")}.");
        output.WriteLine("As traduções e os status não foram alterados.");

        WriteOutcome(
            output,
            summary.Refused,
            "pelo modelo; seguem sem sugestão",
            summary.Failed,
            "sem revisão; rode 'vnf review' de novo.",
            summary.Usage,
            dollars);

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
