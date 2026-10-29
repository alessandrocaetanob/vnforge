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

/// <summary>A fonte do texto japonês (original) nas telas; <see cref="Default"/> é a Noto Sans JP embutida no app.</summary>
public enum JapaneseFontChoice
{
    Default,
    YuGothic,
    Meiryo,
    MsGothic,
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

    /// <summary>Tamanhos do texto do Editor em porcentagem, na ordem de <c>Ctrl</c> <c>+</c>; o padrão é <see cref="DefaultTextScale"/>.</summary>
    public static IReadOnlyList<int> TextScaleSteps { get; } = [90, 100, 110, 125, 150];

    public const int DefaultTextScale = 100;

    public static AppSettings Default { get; } = new(2);

    /// <summary>Tamanho do texto da grade, do inspetor e do Modo revisão, em porcentagem; sempre um dos <see cref="TextScaleSteps"/>.</summary>
    public int TextScalePercent { get; init; } = DefaultTextScale;

    /// <summary>A fonte do japonês nas telas.</summary>
    public JapaneseFontChoice JapaneseFont { get; init; } = JapaneseFontChoice.Default;

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

    /// <summary>Esconde as cenas e os capítulos sem linhas no Editor e no Painel (padrão); desligado, aparecem como prontos.</summary>
    public bool HideEmptyScenes { get; init; } = true;

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

    /// <summary>Por que o <c>app.json</c> existe e não foi lido (ou tem um valor de dinheiro inválido), em português; <see langword="null"/> quando está bem ou não existe.</summary>
    public string? ReadProblem() => AppBudgetFile.Load(System.IO.Path.GetDirectoryName(Path)!).Problem;

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
            var concurrency = ReadInt(root, "concurrency", AppSettings.Default.Concurrency, v => v is >= AppSettings.MinConcurrency and <= AppSettings.MaxConcurrency);
            var theme = ReadEnum(root, "theme", ThemeChoice.System);
            var quality = ReadEnum(root, "quality", TranslationQuality.Careful);
            var textScale = ReadInt(root, "textScale", AppSettings.DefaultTextScale, AppSettings.TextScaleSteps.Contains);
            var japaneseFont = ReadEnum(root, "japaneseFont", JapaneseFontChoice.Default);
            var rate = AppBudgetFile.ReadMoney(root, "exchangeRate", AppSettings.MaxBrlPerUsd);
            return new AppSettings(concurrency, theme)
            {
                BrlPerUsd = rate,
                RateInformedOn = rate is null ? null : AppBudgetFile.ReadDate(root, "exchangeRateOn"),
                SpendLimit = AppBudgetFile.ReadMoney(root, "spendLimit", decimal.MaxValue),
                WarnAbove = AppBudgetFile.ReadMoney(root, "warnAbove", decimal.MaxValue),
                Quality = quality,
                TextScalePercent = textScale,
                JapaneseFont = japaneseFont,
                HideEmptyScenes = !root.TryGetProperty("hideEmptyScenes", out var hide) || hide.ValueKind != JsonValueKind.False,
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    private static int ReadInt(JsonElement root, string name, int fallback, Func<int, bool> isValid) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
        && isValid(parsed)
            ? parsed
            : fallback;

    private static T ReadEnum<T>(JsonElement root, string name, T fallback)
        where T : struct, Enum =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && Enum.TryParse<T>(value.GetString(), ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        // Um arquivo que não pôde ser lido pode ter um limite de gasto da pessoa: guarda uma cópia antes de gravar o padrão por cima.
        if (File.Exists(Path) && ReadProblem() is not null)
        {
            File.Copy(Path, Path + ".bad", overwrite: true);
        }

        var data = new Dictionary<string, object>
        {
            ["concurrency"] = settings.Concurrency,
            ["theme"] = settings.Theme.ToString().ToLowerInvariant(),
            ["quality"] = settings.Quality.ToString().ToLowerInvariant(),
        };
        if (!settings.HideEmptyScenes)
        {
            data["hideEmptyScenes"] = false;
        }

        if (settings.TextScalePercent != AppSettings.DefaultTextScale)
        {
            data["textScale"] = settings.TextScalePercent;
        }

        if (settings.JapaneseFont != JapaneseFontChoice.Default)
        {
            data["japaneseFont"] = settings.JapaneseFont.ToString().ToLowerInvariant();
        }

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

        // Por arquivo temporário e troca: uma queda no meio da gravação não deixa um app.json pela metade, que esconderia o limite de gasto.
        var temp = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
            File.Move(temp, Path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
