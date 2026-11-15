using System.Text;
using System.Text.Json;
using VNForge.Core;
using VNForge.Core.Library;

namespace VNForge.AI.Explain;

/// <summary>Um pedaço do original com o que ele significa: <c>今日は</c> · hoje.</summary>
public sealed record ExplainedPart(string Source, string Meaning);

/// <summary>A explicação de uma fala: o original quebrado em partes, só para a pessoa ler.</summary>
/// <param name="Parts">As partes, na ordem do original.</param>
/// <param name="Model">O modelo que explicou, como a API o conhece.</param>
/// <param name="FromCache">Veio de <c>.cache/explain.json</c>, sem chamar a API nem gastar.</param>
public sealed record LineExplanation(IReadOnlyList<ExplainedPart> Parts, string Model, bool FromCache);

/// <summary>
/// <c>.cache/explain.json</c>: as explicações já pedidas, pelo hash do original. Um cache que não abre é recusado com <see cref="InvalidFileException"/>
/// e o arquivo fica como está: seguir com um cache vazio faria o próximo Save gravar por cima das explicações já pagas (R7).
/// </summary>
internal sealed class ExplainCache
{
    private readonly Dictionary<string, LineExplanation> entries = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <exception cref="InvalidFileException">O arquivo existe e está vazio, corrompido ou fora do formato; não é alterado.</exception>
    public static ExplainCache Load(string path)
    {
        var cache = new ExplainCache();
        if (!File.Exists(path))
        {
            return cache;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (Read(entry.Value) is { } explanation)
                {
                    cache.entries[entry.Name] = explanation;
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new InvalidFileException(path, "o cache de explicações está vazio ou corrompido. O arquivo não foi alterado; apague-o para refazer o cache, e as explicações já pagas serão cobradas de novo.", e);
        }

        return cache;
    }

    public bool TryGet(string hash, out LineExplanation explanation)
    {
        lock (gate)
        {
            return entries.TryGetValue(hash, out explanation!);
        }
    }

    public void Set(string hash, LineExplanation explanation)
    {
        lock (gate)
        {
            entries[hash] = explanation with { FromCache = false };
        }
    }

    /// <summary>Grava por <see cref="AtomicFile"/> (temporário, troca e releitura), para uma queda ou um disco que zera o rename não perder o anterior.</summary>
    public void Save(string path)
    {
        string json;
        lock (gate)
        {
            var snapshot = new SortedDictionary<string, object>(entries.ToDictionary(
                e => e.Key,
                e => (object)new { model = e.Value.Model, parts = e.Value.Parts.Select(p => new { source = p.Source, meaning = p.Meaning }) },
                StringComparer.Ordinal), StringComparer.Ordinal);
            json = JsonSerializer.Serialize(snapshot);
        }

        AtomicFile.WriteAllText(path, json);
    }

    private static LineExplanation? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<ExplainedPart>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String
                && part.TryGetProperty("meaning", out var meaning) && meaning.ValueKind == JsonValueKind.String)
            {
                list.Add(new ExplainedPart(source.GetString()!, meaning.GetString()!));
            }
        }

        return list.Count == 0 ? null : new LineExplanation(list, model.GetString()!, FromCache: true);
    }
}
