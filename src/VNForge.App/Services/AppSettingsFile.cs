using System.Globalization;
using System.Text.Json;
using VNForge.AI.Cost;
using VNForge.AI.Settings;

namespace VNForge.App.Services;

/// <summary>Tema do app: segue o Windows (padrão), claro ou escuro.</summary>
public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>Ajustes do app que valem para o usuário e não para um projeto. Nunca contêm a chave de API.</summary>
/// <param name="Concurrency">Chamadas à IA ao mesmo tempo, de <see cref="MinConcurrency"/> a <see cref="MaxConcurrency"/>.</param>
/// <param name="Theme">Tema escolhido nos Ajustes; <see cref="ThemeChoice.System"/> segue o Windows.</param>
public sealed record AppSettings(int Concurrency, ThemeChoice Theme = ThemeChoice.System)
{
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 8;

    /// <summary>Teto da cotação aceita: acima disso é quase certo um erro de digitação.</summary>
    public const decimal MaxBrlPerUsd = AppBudgetFile.MaxBrlPerUsd;

    public static AppSettings Default { get; } = new(2);

    /// <summary>Reais por dólar, como a pessoa informou; <see langword="null"/> enquanto ela não informou.</summary>
    public decimal? BrlPerUsd { get; init; }

    /// <summary>O dia em que a pessoa informou a cotação.</summary>
    public DateOnly? RateInformedOn { get; init; }

    /// <summary>Limite de gasto em reais por projeto; <see langword="null"/> não limita.</summary>
    public decimal? SpendLimit { get; init; }

    /// <summary>O app pergunta antes de um pedido que custa mais que isto, em reais; <see langword="null"/> nunca pergunta.</summary>
    public decimal? WarnAbove { get; init; }

    /// <summary>A qualidade escolhida por último; o padrão é a mais cuidadosa, que usa o modelo padrão do projeto.</summary>
    public TranslationQuality Quality { get; init; } = TranslationQuality.Careful;

    /// <summary>A cotação para converter custos; sem uma informada, o valor de exemplo (<see cref="ExchangeRate.Placeholder"/>).</summary>
    public ExchangeRate Rate => BrlPerUsd is { } value ? new ExchangeRate(value, RateInformedOn) : ExchangeRate.Placeholder;

    public BudgetLimits Budget => new(SpendLimit, WarnAbove);
}

/// <summary>
/// Lê e grava <c>app.json</c> na pasta de configuração do usuário (<c>%APPDATA%/VNForge</c>). É uma conveniência: um arquivo ilegível
/// vale como o padrão e o próximo <see cref="Save"/> o substitui.
/// </summary>
public sealed class AppSettingsFile(string directory)
{
    private const string FileName = "app.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    public AppSettings Load()
    {
        if (!File.Exists(Path))
        {
            return AppSettings.Default;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return AppSettings.Default;
            }

            // Cada campo vale sozinho: um tema ilegível não derruba a concorrência salva, e vice-versa.
            var concurrency = root.TryGetProperty("concurrency", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var parsed)
                && parsed is >= AppSettings.MinConcurrency and <= AppSettings.MaxConcurrency
                    ? parsed
                    : AppSettings.Default.Concurrency;
            var theme = root.TryGetProperty("theme", out var themeValue)
                && themeValue.ValueKind == JsonValueKind.String
                && Enum.TryParse<ThemeChoice>(themeValue.GetString(), ignoreCase: true, out var chosen)
                && Enum.IsDefined(chosen)
                    ? chosen
                    : ThemeChoice.System;
            var quality = root.TryGetProperty("quality", out var qualityValue)
                && qualityValue.ValueKind == JsonValueKind.String
                && Enum.TryParse<TranslationQuality>(qualityValue.GetString(), ignoreCase: true, out var picked)
                && Enum.IsDefined(picked)
                    ? picked
                    : TranslationQuality.Careful;
            var rate = AppBudgetFile.ReadMoney(root, "exchangeRate", AppSettings.MaxBrlPerUsd);
            return new AppSettings(concurrency, theme)
            {
                BrlPerUsd = rate,
                RateInformedOn = rate is null ? null : AppBudgetFile.ReadDate(root, "exchangeRateOn"),
                SpendLimit = AppBudgetFile.ReadMoney(root, "spendLimit", decimal.MaxValue),
                WarnAbove = AppBudgetFile.ReadMoney(root, "warnAbove", decimal.MaxValue),
                Quality = quality,
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var data = new Dictionary<string, object>
        {
            ["concurrency"] = settings.Concurrency,
            ["theme"] = settings.Theme.ToString().ToLowerInvariant(),
            ["quality"] = settings.Quality.ToString().ToLowerInvariant(),
        };
        if (settings.BrlPerUsd is { } rate)
        {
            data["exchangeRate"] = rate;
            if (settings.RateInformedOn is { } on)
            {
                data["exchangeRateOn"] = on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        if (settings.SpendLimit is { } limit)
        {
            data["spendLimit"] = limit;
        }

        if (settings.WarnAbove is { } warn)
        {
            data["warnAbove"] = warn;
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(data, Json));
    }
}
