using System.Text.Json;

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

    public static AppSettings Default { get; } = new(2);
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
            return new AppSettings(concurrency, theme);
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
        File.WriteAllText(Path, JsonSerializer.Serialize(new Dictionary<string, object> { ["concurrency"] = settings.Concurrency, ["theme"] = settings.Theme.ToString().ToLowerInvariant() }, Json));
    }
}
