using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Um script que o build anterior reconstruiu: o hash do que entrou nele e o tamanho do resultado guardado em <c>.cache/build-scripts/</c>.</summary>
/// <param name="Hash">Hash do script original, da estratégia de codificação e das unidades que o build grava (<see cref="BuildState.HashInputs"/>).</param>
/// <param name="Identical">Verdadeiro quando a reconstrução devolveu os bytes do original: não há arquivo guardado.</param>
/// <param name="Length">Tamanho do arquivo guardado; confere o arquivo antes de reaproveitá-lo.</param>
internal sealed record BuildStateScript(string Hash, bool Identical, long Length);

/// <summary>Um pacote gravado em <c>build/</c> pelo build anterior.</summary>
internal sealed record BuildStateArchive(string GamePath, int ReplacedFiles, long Length);

/// <summary>
/// O que o último <c>vnf build</c> deixou em <c>.cache/build-state.json</c>, para o build incremental. Cada script reconstruído guarda o hash
/// de suas entradas e os bytes resultantes em <c>.cache/build-scripts/&lt;chave&gt;-&lt;hash&gt;.bin</c>; o nome leva o hash, então um build
/// interrompido não deixa um arquivo novo sob o hash antigo. O estado só é regravado depois que o <c>build/</c> novo está no lugar.
/// </summary>
internal sealed record BuildState(
    int Version,
    Dictionary<string, BuildStateScript> Scripts,
    List<BuildStateArchive> Archives,
    List<string> Warnings,
    string? TextFingerprint = null)
{
    internal const int CurrentVersion = 1;

    internal const string StateFileName = "build-state.json";

    private const string ScriptsDirectoryName = "build-scripts";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Estado salvo, ou <c>null</c> quando não há, não lê ou é de outra versão: o build então reconstrói tudo.</summary>
    public static BuildState? Load(ProjectFolder project)
    {
        try
        {
            var path = Path.Combine(project.CacheDirectory, StateFileName);
            var state = File.Exists(path) ? JsonSerializer.Deserialize<BuildState>(File.ReadAllText(path, Encoding.UTF8), Json) : null;
            return state is { Version: CurrentVersion, Scripts: not null, Archives: not null, Warnings: not null } ? state : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string ScriptPath(ProjectFolder project, string key, string hash) =>
        Path.Combine(project.CacheDirectory, ScriptsDirectoryName, $"{key}-{hash}.bin");

    /// <summary>
    /// Impressão digital do que o build lê de <c>text/*.json</c> e do perfil de formatação: a estratégia, o <c>formatting.json</c> e, de cada unidade
    /// que o build grava, o que entra nos bytes. Mudar status, notas ou origem não a altera, e copiar o projeto sem preservar datas também não.
    /// O Painel a compara com a gravada no último build para saber se o <c>build/</c> ainda vale.
    /// </summary>
    public static string FingerprintTexts(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, string encoderId, string formattingPath, string? gameTitle = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, encoderId);
        Append(hash, gameTitle);
        Append(hash, File.Exists(formattingPath) ? Convert.ToBase64String(File.ReadAllBytes(formattingPath)) : null);
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var unit in units.Where(u => u.NeedsRewrite))
            {
                Append(hash, key);
                Append(hash, unit.Id);
                Append(hash, unit.Kind.ToString());
                Append(hash, unit.ChosenSpeaker);
                Append(hash, unit.Source);
                Append(hash, unit.Translation);
                Append(hash, unit.Locator?.GetRawText());
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..32].ToLowerInvariant();
    }

    /// <summary>Hash do que determina os bytes do arquivo do título: o original, a estratégia e o título que o build grava.</summary>
    public static string HashTitle(byte[] original, string encoderId, string title)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(original);
        Append(hash, encoderId);
        Append(hash, typeof(BuildState).Assembly.GetName().Version?.ToString());
        Append(hash, title);
        return Convert.ToHexString(hash.GetHashAndReset())[..32].ToLowerInvariant();
    }

    /// <summary>Hash do que determina os bytes de um script reconstruído: o original, a estratégia e cada unidade que o build grava.</summary>
    public static string HashInputs(byte[] original, string encoderId, IEnumerable<TextUnit> units)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(original);
        Append(hash, encoderId);
        Append(hash, typeof(BuildState).Assembly.GetName().Version?.ToString());
        foreach (var unit in units.Where(u => u.NeedsRewrite))
        {
            Append(hash, unit.Id);
            Append(hash, unit.Kind.ToString());
            Append(hash, unit.Speaker);
            Append(hash, unit.ChosenSpeaker);
            Append(hash, unit.Source);
            Append(hash, unit.Translation);
            Append(hash, unit.Locator?.GetRawText());
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..32].ToLowerInvariant();
    }

    /// <summary>Os bytes guardados do script, ou <c>null</c> se o arquivo sumiu ou não tem o tamanho registrado.</summary>
    public static byte[]? ReadScript(ProjectFolder project, string key, BuildStateScript entry)
    {
        var path = ScriptPath(project, key, entry.Hash);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length == entry.Length ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void WriteScript(ProjectFolder project, string key, string hash, byte[] content)
    {
        var path = ScriptPath(project, key, hash);
        Directory.CreateDirectory(Path.Combine(project.CacheDirectory, ScriptsDirectoryName));
        File.WriteAllBytes(path, content);
    }

    /// <summary>Verdadeiro quando cada pacote do estado ainda está em <c>build/</c> com o tamanho registrado.</summary>
    public bool ArchivesIntact(ProjectFolder project) =>
        Archives.Count > 0
        && Archives.All(a =>
        {
            var file = new FileInfo(Path.Combine(project.BuildDirectory, a.GamePath.Replace('/', Path.DirectorySeparatorChar)));
            return file.Exists && file.Length == a.Length;
        });

    /// <summary>Como <see cref="Save"/>, mas uma falha de disco não derruba um build já gravado: o próximo build só reconstrói mais.</summary>
    public void TrySave(ProjectFolder project)
    {
        try
        {
            Save(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem estado o próximo build reconstrói tudo; o build/ já gravado continua válido.
        }
    }

    /// <summary>Grava o estado e apaga os arquivos de script que ele não cita mais.</summary>
    public void Save(ProjectFolder project)
    {
        Directory.CreateDirectory(project.CacheDirectory);
        var path = Path.Combine(project.CacheDirectory, StateFileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json), Encoding.UTF8);
        File.Move(temp, path, overwrite: true);

        var directory = Path.Combine(project.CacheDirectory, ScriptsDirectoryName);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var keep = Scripts.Where(s => !s.Value.Identical).Select(s => Path.GetFileName(ScriptPath(project, s.Key, s.Value.Hash))).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory).Where(f => !keep.Contains(Path.GetFileName(f))))
        {
            File.Delete(file);
        }
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        hash.AppendData(BitConverter.GetBytes(value is null ? -1 : bytes.Length));
        hash.AppendData(bytes);
    }
}
