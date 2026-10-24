using System.Text;
using System.Text.Json;

namespace VNForge.Core.Projects;

/// <summary>
/// Tokens e custo em dólares de uma ou mais execuções de IA do projeto. <see cref="Dollars"/> soma só as execuções de modelo com preço conhecido;
/// <see cref="UnpricedRuns"/> conta as outras, cujo custo é desconhecido (não zero).
/// </summary>
public sealed record AiUsageTotals(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal Dollars, int Runs, int UnpricedRuns = 0)
{
    public static AiUsageTotals Empty { get; } = new(0, 0, 0, 0, 0m, 0);

    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;

    public static AiUsageTotals operator +(AiUsageTotals a, AiUsageTotals b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadTokens + b.CacheReadTokens,
        a.CacheWriteTokens + b.CacheWriteTokens,
        a.Dollars + b.Dollars,
        a.Runs + b.Runs,
        a.UnpricedRuns + b.UnpricedRuns);
}

/// <summary>
/// O gasto acumulado de IA do projeto em <c>.cache/ai-usage.json</c>: <c>vnf translate</c> e <c>vnf review</c> somam o que cada execução gastou,
/// e o Painel do app lê o total. Apagar o <c>.cache/</c> zera a conta.
/// </summary>
public static class AiUsageLog
{
    private const string FileName = "ai-usage.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private enum ReadOutcome
    {
        Ok,
        Missing,
        Corrupt,
        Unreadable,
    }

    public static string PathFor(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.CacheDirectory, FileName);
    }

    /// <summary>O total gravado, ou <see cref="AiUsageTotals.Empty"/> quando não há arquivo ou ele não é legível: o Painel não trava por causa da conta.</summary>
    public static AiUsageTotals Load(ProjectFolder project) =>
        TryRead(PathFor(project), out var totals) == ReadOutcome.Ok ? totals : AiUsageTotals.Empty;

    /// <summary>
    /// Soma <paramref name="run"/> ao total gravado (conta como uma execução). Arquivo que não pôde ser lido agora (em uso, sem permissão) deixa o gasto
    /// sem registro em vez de apagar o histórico; arquivo de JSON inválido vai para <c>ai-usage.json.bad</c> e a conta recomeça. Uma falha de disco
    /// não derruba o comando que já gastou.
    /// </summary>
    public static void TryRecord(ProjectFolder project, AiUsageTotals run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var path = PathFor(project);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var current = AiUsageTotals.Empty;
            switch (TryRead(path, out var saved))
            {
                case ReadOutcome.Ok:
                    current = saved;
                    break;
                case ReadOutcome.Unreadable:
                    return;
                case ReadOutcome.Corrupt:
                    File.Move(path, path + ".bad", overwrite: true);
                    break;
            }

            Directory.CreateDirectory(project.CacheDirectory);
            File.WriteAllText(temp, JsonSerializer.Serialize(current + (run with { Runs = 1 }), Json) + "\n", new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O gasto fica sem registro; o resultado do comando já saiu.
            TryDelete(temp);
        }
    }

    private static ReadOutcome TryRead(string path, out AiUsageTotals totals)
    {
        totals = AiUsageTotals.Empty;
        try
        {
            if (!File.Exists(path))
            {
                return ReadOutcome.Missing;
            }

            var read = JsonSerializer.Deserialize<AiUsageTotals>(File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true)), Json);
            if (read is not { InputTokens: >= 0, OutputTokens: >= 0, CacheReadTokens: >= 0, CacheWriteTokens: >= 0, Dollars: >= 0m, Runs: >= 0, UnpricedRuns: >= 0 })
            {
                return ReadOutcome.Corrupt;
            }

            totals = read;
            return ReadOutcome.Ok;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            return ReadOutcome.Corrupt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ReadOutcome.Unreadable;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sobra um .tmp com nome único; não atrapalha a próxima gravação.
        }
    }
}
