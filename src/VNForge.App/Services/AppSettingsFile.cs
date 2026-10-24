using System.Text.Json;

namespace VNForge.App.Services;

/// <summary>Ajustes do app que valem para o usuário e não para um projeto. Nunca contêm a chave de API.</summary>
/// <param name="Concurrency">Chamadas à IA ao mesmo tempo, de <see cref="MinConcurrency"/> a <see cref="MaxConcurrency"/>.</param>
public sealed record AppSettings(int Concurrency)
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
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("concurrency", out var value)
                && value.TryGetInt32(out var concurrency)
                && concurrency is >= AppSettings.MinConcurrency and <= AppSettings.MaxConcurrency
                    ? new AppSettings(concurrency)
                    : AppSettings.Default;
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
        File.WriteAllText(Path, JsonSerializer.Serialize(new Dictionary<string, int> { ["concurrency"] = settings.Concurrency }, Json));
    }
}
