using System.Globalization;
using System.Text.RegularExpressions;
using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary>Modelo, esforço, lote e paralelismo de uma execução de IA (<c>vnf translate</c> e <c>vnf review</c>).</summary>
internal sealed record AiRunSettings(string Model, ClaudeEffort Effort, int BatchSize, int Concurrency);

/// <summary>Leitura de opções e formatação de mensagens que os comandos de IA dividem.</summary>
internal static class AiCommandSupport
{
    private const int ProgressBarWidth = 20;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Modelo: opção, depois <c>vnf config ai</c>, depois <c>vnforge.json</c>. Esforço e lote caem no <c>vnforge.json</c>.</summary>
    public static AiRunSettings ReadSettings(
        VnfCli cli, AiConfig ai, string? modelOption, string? effortOption, int? batchSizeOption, int? concurrencyOption)
    {
        var settingsFile = new AiUserSettingsFile(cli.UserConfigDirectory);
        var model = modelOption
            ?? (File.Exists(settingsFile.Path) ? settingsFile.Load().Model : null)
            ?? (ClaudeModels.All.Contains(ai.Model) ? ai.Model : ClaudeModels.Opus);
        if (!ClaudeModels.All.Contains(model))
        {
            throw new UsageException($"Modelo desconhecido: '{model}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
        }

        var effort = ParseEffort(effortOption ?? ai.Effort, strict: effortOption is not null);

        var batchSize = batchSizeOption ?? ai.BatchSize;
        if (batchSize < TranslationRunOptions.MinBatchSize || batchSize > TranslationRunOptions.MaxBatchSize)
        {
            var origin = batchSizeOption is null ? "ai.batchSize do vnforge.json" : "--batch-size";
            throw new UsageException($"{origin} precisa ficar entre {TranslationRunOptions.MinBatchSize} e {TranslationRunOptions.MaxBatchSize} (recebeu {batchSize}).");
        }

        var concurrency = concurrencyOption ?? 2;
        if (concurrency < 1)
        {
            throw new UsageException("--concurrency precisa ser 1 ou maior.");
        }

        return new AiRunSettings(model, effort, batchSize, concurrency);
    }

    public static string RequireApiKey(VnfCli cli) =>
        ApiKeyResolver.Resolve(cli.ApiKeyStore).Key
            ?? throw new UsageException(
                $"Sem chave de API. Defina {ApiKeyResolver.EnvironmentVariable} ou rode 'vnf config ai --set-key'. Use --dry-run para só estimar o custo.");

    /// <summary>Glob sobre a chave do script (<c>*</c> e <c>?</c>), sem diferenciar maiúsculas; <c>.json</c> no fim do padrão é ignorado.</summary>
    public static Regex? ParseFiles(string? text)
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

    public static string ProgressLine(int done, int total, string key)
    {
        var fraction = total == 0 ? 1.0 : Math.Min(1.0, (double)done / total);
        var filled = (int)Math.Floor(fraction * ProgressBarWidth);
        var bar = new string('#', filled) + new string('-', ProgressBarWidth - filled);
        return $"[{bar}] {(int)Math.Floor(fraction * 100)}%  {Number(done)}/{Number(total)}  {key}.json";
    }

    public static string Count(int value, string singular, string plural) => $"{Number(value)} {(value == 1 ? singular : plural)}";

    public static string Number(long value) => value.ToString("N0", PortugueseBrazil);

    public static string Tokens(long value) => value >= 1_000_000
        ? string.Create(PortugueseBrazil, $"{value / 1_000_000.0:0.0} M tokens")
        : $"{Number(value)} tokens";

    public static string Dollars(decimal value) => string.Create(PortugueseBrazil, $"US$ {value:0.00}");

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
}

/// <summary>
/// As linhas que um comando de IA vai processar e o mapa para gravá-las de volta: o conteúdo de cada <c>text/*.json</c> tocado
/// e, por id, a chave do arquivo e o índice da linha.
/// </summary>
internal sealed class LineSelection
{
    private const int PreviousLinesWindow = 30;

    public Dictionary<string, List<TextUnit>> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> KeyById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> IndexById { get; } = new(StringComparer.Ordinal);

    public List<TextUnit> Pending { get; } = [];

    /// <param name="filter">Glob sobre a chave do script; <c>null</c> aceita todas.</param>
    /// <param name="limit">Máximo de linhas; <c>null</c> sem limite.</param>
    /// <param name="wanted">Quais linhas de um script entram.</param>
    public static LineSelection Build(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts, Regex? filter, int? limit, Func<TextUnit, bool> wanted)
    {
        var selection = new LineSelection();
        foreach (var (key, units) in texts)
        {
            if (filter is not null && !filter.IsMatch(key))
            {
                continue;
            }

            var selected = units.Where(wanted).ToList();
            if (selected.Count == 0 || (limit is { } max && selection.Pending.Count >= max))
            {
                continue;
            }

            selection.Files[key] = [.. units];
            for (var i = 0; i < units.Count; i++)
            {
                selection.KeyById[units[i].Id] = key;
                selection.IndexById[units[i].Id] = i;
            }

            selection.Pending.AddRange(limit is { } cap ? selected.Take(cap - selection.Pending.Count) : selected);
        }

        return selection;
    }

    /// <summary>Monta o lote com as linhas anteriores do script como contexto; chame sob o lock que protege <see cref="Files"/>.</summary>
    public TranslationBatch BuildBatch(TranslationBatchBuilder builder, IReadOnlyList<TextUnit> lines)
    {
        var first = lines[0].Id;
        var previous = Files[KeyById[first]].Take(IndexById[first]).TakeLast(PreviousLinesWindow).ToList();
        return builder.Build(lines, previous);
    }
}
