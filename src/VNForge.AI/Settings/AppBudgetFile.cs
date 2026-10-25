using System.Globalization;
using System.Text.Json;
using VNForge.AI.Cost;

namespace VNForge.AI.Settings;

/// <summary>A cotação, o limite de gasto e o valor de aviso que a pessoa guardou em <c>app.json</c>.</summary>
public sealed record BudgetSettings(ExchangeRate Rate, BudgetLimits Limits);

/// <summary>O que <see cref="AppBudgetFile.Load"/> encontrou.</summary>
/// <param name="Settings">O orçamento lido; "sem limite e cotação de exemplo" quando o arquivo não existe ou não pôde ser lido.</param>
/// <param name="Problem">Por que o arquivo existe mas não pôde ser lido, em português; <see langword="null"/> quando ele foi lido ou não existe.</param>
public sealed record BudgetFileReading(BudgetSettings Settings, string? Problem);

/// <summary>
/// Lê do <c>app.json</c> da pasta de configuração do usuário só o que o orçamento usa. O app grava esse arquivo (Ajustes) e a CLI o lê, para que
/// <c>vnf translate</c> respeite o mesmo limite. Arquivo ausente vale como "sem limite e cotação de exemplo"; arquivo que existe e não pode ser
/// lido também, mas o resultado traz o motivo, porque ele pode esconder um limite que a pessoa pôs e quem gasta precisa saber.
/// </summary>
public static class AppBudgetFile
{
    public const string FileName = "app.json";

    /// <summary>Teto da cotação aceita: acima disso é quase certo um erro de digitação.</summary>
    public const decimal MaxBrlPerUsd = 100m;

    public static BudgetFileReading Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.Combine(directory, FileName);
        var none = new BudgetSettings(ExchangeRate.Placeholder, BudgetLimits.None);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                ? new BudgetFileReading(Read(root), null)
                : new BudgetFileReading(none, $"{path} não é um objeto JSON.");
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new BudgetFileReading(none, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new BudgetFileReading(none, $"{path} não pôde ser lido: {e.Message}");
        }
    }

    /// <summary>Lê os campos de dinheiro e a data do <c>app.json</c> já aberto.</summary>
    public static BudgetSettings Read(JsonElement root)
    {
        var rate = ReadMoney(root, "exchangeRate", MaxBrlPerUsd);
        var informedOn = rate is null ? null : ReadDate(root, "exchangeRateOn");
        return new BudgetSettings(
            rate is { } value ? new ExchangeRate(value, informedOn) : ExchangeRate.Placeholder,
            new BudgetLimits(ReadMoney(root, "spendLimit", decimal.MaxValue), ReadMoney(root, "warnAbove", decimal.MaxValue)));
    }

    /// <summary>Um valor positivo até <paramref name="max"/>; qualquer outra coisa (zero, texto, negativo, grande demais) vale como "não informado".</summary>
    public static decimal? ReadMoney(JsonElement root, string name, decimal max) =>
        root.TryGetProperty(name, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetDecimal(out var value)
        && value <= max
        && value > 0m
            ? value
            : null;

    public static DateOnly? ReadDate(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element)
        && element.ValueKind == JsonValueKind.String
        && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
