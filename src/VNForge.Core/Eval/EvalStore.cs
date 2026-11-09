using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Projects;

namespace VNForge.Core.Eval;

/// <summary>
/// As rodadas do banco de provas e o conjunto de configurações do projeto, em <c>.cache/eval/</c>. É cache: apagar a pasta só faz o app esquecer as rodadas.
/// Nada aqui toca <c>text/</c>: o banco de provas nunca grava tradução no projeto.
/// </summary>
public static class EvalStore
{
    private const string FolderName = "eval";
    private const string RoundsFolderName = "rounds";
    private const string ConfigsFileName = "configs.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string DirectoryFor(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.CacheDirectory, FolderName);
    }

    public static string RoundPath(ProjectFolder project, string roundId) =>
        Path.Combine(DirectoryFor(project), RoundsFolderName, $"{roundId}.json");

    /// <summary>O id de uma rodada que começa em <paramref name="at"/>: data e hora em UTC, que ordenam como texto.</summary>
    public static string NewRoundId(DateTimeOffset at) => at.UtcDateTime.ToString("yyyyMMdd'-'HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    public static void SaveRound(ProjectFolder project, EvalRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        Write(RoundPath(project, round.Id), JsonSerializer.Serialize(round with { Version = EvalRound.CurrentVersion }, Json));
    }

    /// <summary>A rodada mais recente; nula sem rodadas ou quando o arquivo dela não é legível (a tela não trava por causa dele).</summary>
    public static EvalRound? LoadLatest(ProjectFolder project)
    {
        var folder = Path.Combine(DirectoryFor(project), RoundsFolderName);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderByDescending(p => p, StringComparer.Ordinal))
        {
            if (TryRead<EvalRound>(path) is { Version: EvalRound.CurrentVersion } round)
            {
                return round;
            }
        }

        return null;
    }

    /// <summary>As configurações que a pessoa montou; vazio quando ela ainda não gravou nenhuma.</summary>
    public static IReadOnlyList<EvalConfig> LoadConfigs(ProjectFolder project)
    {
        var path = Path.Combine(DirectoryFor(project), ConfigsFileName);
        return File.Exists(path) && TryRead<List<EvalConfig>>(path) is { Count: > 0 } configs ? configs : [];
    }

    public static void SaveConfigs(ProjectFolder project, IReadOnlyList<EvalConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);
        Write(Path.Combine(DirectoryFor(project), ConfigsFileName), JsonSerializer.Serialize(configs, Json));
    }

    private static T? TryRead<T>(string path)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, content + "\n", new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
