using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.Core;

namespace VNForge.AI.Settings;

/// <summary>Erro esperado ao ler ou gravar as configurações de IA do usuário; a mensagem vai direto ao usuário.</summary>
public sealed class AiSettingsException(string message, Exception? innerException = null) : VNForgeException(message, innerException);

/// <summary>Configurações de IA do usuário, guardadas fora de qualquer projeto. Nunca contêm a chave de API.</summary>
public sealed record AiUserSettings(string Model)
{
    public static AiUserSettings Default { get; } = new(ClaudeModels.Opus);
}

/// <summary>Lê e grava <c>ai.json</c> na pasta de configuração do usuário (<c>%APPDATA%/VNForge</c>).</summary>
public sealed class AiUserSettingsFile(string directory)
{
    private const string FileName = "ai.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    /// <summary>Pasta de configuração padrão: <c>%APPDATA%/VNForge</c>, ou a pasta temporária se o sistema não informar uma absoluta.</summary>
    public static string DefaultDirectory()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDirectory = !string.IsNullOrWhiteSpace(roaming) && System.IO.Path.IsPathRooted(roaming)
            ? roaming
            : System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(baseDirectory, "VNForge");
    }

    public AiUserSettings Load()
    {
        if (!File.Exists(Path))
        {
            return AiUserSettings.Default;
        }

        try
        {
            var model = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path))?.GetValueOrDefault("model");
            return model is not null && ClaudeModels.All.Contains(model)
                ? new AiUserSettings(model)
                : throw new AiSettingsException($"O modelo em {Path} não é válido. Use um de: {string.Join(", ", ClaudeModels.All)}.");
        }
        catch (JsonException e)
        {
            throw new AiSettingsException($"O arquivo {Path} não é um JSON válido.", e);
        }
    }

    public void Save(AiUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(new Dictionary<string, string> { ["model"] = settings.Model }, Json));
    }
}
