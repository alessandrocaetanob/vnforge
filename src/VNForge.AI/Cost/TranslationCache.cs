using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>
/// Cache de traduções pelo hash da linha (tipo, falante e original) mais um contexto (modelo, guia e escolhas de estilo, glossário,
/// personagens). Mudar o original ou o contexto muda a chave, então a linha é traduzida de novo; repetir a mesma tradução
/// não chama a API. As falas vizinhas (contexto anterior e seguinte) não entram na chave: uma tradução em cache vale
/// enquanto a linha e o contexto fixo do projeto não mudarem.
/// </summary>
public sealed class TranslationCache
{
    private readonly Dictionary<string, string> _entries;
    private readonly Lock _gate = new();

    public TranslationCache() => _entries = new(StringComparer.Ordinal);

    private TranslationCache(Dictionary<string, string> entries) => _entries = entries;

    public int Count { get { lock (_gate) return _entries.Count; } }

    public static string KeyFor(TextUnit unit, string context)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var raw = string.Join('\u001f', context, unit.Kind, unit.Speaker ?? string.Empty, unit.Source);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>Contexto que invalida o cache: o que, fora as linhas, muda a tradução.</summary>
    public static string ContextFor(string model, TranslationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var sb = new StringBuilder(model).Append('\u001e').Append(batch.StyleGuide.Trim()).Append('\u001e').Append(batch.Style.CacheKey());
        foreach (var g in batch.Glossary)
            sb.Append('\u001e').Append(g.Source).Append('>').Append(g.Target);
        foreach (var c in batch.Characters)
            sb.Append('\u001e').Append(c.Source).Append('>').Append(c.Target).Append('|').Append(c.Tone);
        return sb.ToString();
    }

    public bool TryGet(string key, out string translation)
    {
        lock (_gate)
            return _entries.TryGetValue(key, out translation!);
    }

    public void Set(string key, string translation)
    {
        lock (_gate)
            _entries[key] = translation;
    }

    public static TranslationCache Load(string path)
    {
        if (!File.Exists(path))
            return new TranslationCache();
        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                ?? throw new JsonException("Cache vazio.");
            return new TranslationCache(new Dictionary<string, string>(entries.Where(e => e.Value is not null), StringComparer.Ordinal));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"O cache de traduções '{path}' está corrompido; apague o arquivo para refazê-lo.", e);
        }
    }

    /// <summary>Grava o cache num arquivo temporário e o move por cima do destino, para uma queda no meio não corromper o anterior.</summary>
    public void Save(string path)
    {
        SortedDictionary<string, string> snapshot;
        lock (_gate)
            snapshot = new(_entries, StringComparer.Ordinal);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null)
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
