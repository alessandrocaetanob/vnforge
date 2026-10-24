using System.Text;
using System.Text.Json;

namespace VNForge.Core.Projects;

/// <summary>Tokens e custo em dólares de uma ou mais execuções de IA do projeto.</summary>
public sealed record AiUsageTotals(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal Dollars, int Runs)
{
    public static AiUsageTotals Empty { get; } = new(0, 0, 0, 0, 0m, 0);

    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;

    public static AiUsageTotals operator +(AiUsageTotals a, AiUsageTotals b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadTokens + b.CacheReadTokens,
        a.CacheWriteTokens + b.CacheWriteTokens,
        a.Dollars + b.Dollars,
        a.Runs + b.Runs);
}

/// <summary>
/// O gasto acumulado de IA do projeto em <c>.cache/ai-usage.json</c>: <c>vnf translate</c> e <c>vnf review</c> somam o que cada execução gastou,
/// e o Painel do app lê o total. Apagar o <c>.cache/</c> zera a conta.
/// </summary>
public static class AiUsageLog
{
    private const string FileName = "ai-usage.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string PathFor(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.CacheDirectory, FileName);
    }

    /// <summary>O total gravado, ou <see cref="AiUsageTotals.Empty"/> quando não há arquivo ou ele não é legível: a conta recomeça em vez de travar o Painel.</summary>
    public static AiUsageTotals Load(ProjectFolder project)
    {
        var path = PathFor(project);
        try
        {
            var totals = File.Exists(path) ? JsonSerializer.Deserialize<AiUsageTotals>(File.ReadAllText(path, Encoding.UTF8), Json) : null;
            return totals is { InputTokens: >= 0, OutputTokens: >= 0, CacheReadTokens: >= 0, CacheWriteTokens: >= 0, Dollars: >= 0m, Runs: >= 0 } ? totals : AiUsageTotals.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return AiUsageTotals.Empty;
        }
    }

    /// <summary>Soma <paramref name="run"/> ao total gravado (conta como uma execução). Uma falha de disco não derruba o comando que já gastou: o gasto fica sem registro.</summary>
    public static void TryRecord(ProjectFolder project, AiUsageTotals run)
    {
        ArgumentNullException.ThrowIfNull(run);
        try
        {
            var total = Load(project) + run with { Runs = 1 };
            Directory.CreateDirectory(project.CacheDirectory);
            var path = PathFor(project);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(total, Json) + "\n", new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O gasto fica sem registro; o resultado do comando já saiu.
        }
    }
}
