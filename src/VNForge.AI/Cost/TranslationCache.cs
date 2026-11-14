using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>
/// Cache de traduções pelo hash da linha (tipo, falante e original) mais um contexto (modelo, guia e escolhas de estilo, glossário,
/// personagens). Mudar o original ou o contexto muda a chave, então a linha é traduzida de novo; repetir a mesma tradução
/// não chama a API. As falas vizinhas em si não entram na chave, mas o tamanho da janela de contexto
/// (<c>ai.contextBefore</c> e <c>ai.contextAfter</c>) entra quando difere do padrão 2/2, para uma janela maior não devolver o texto feito com a menor.
/// Com o padrão a chave é a de antes da janela existir, e nenhum cache existente se perde.
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
        foreach (var a in batch.Addressing)
            sb.Append('\u001e').Append(a.From).Append('>').Append(a.To).Append('|').Append(a.Source).Append('=').Append(a.Target);
        if (batch.Window != ContextWindow.Default)
            sb.Append('\u001e').Append("janela:").Append(batch.Window.Before).Append('/').Append(batch.Window.After);

        // O resumo da cena entra no pedido do lote inteiro, então editá-lo (ou gerar a bíblia) invalida todas as linhas do lote, não só as da cena editada.
        // A sinopse, que vale para o jogo inteiro, fica de fora. Sem bíblia o lote não tem cenas e a chave é a de antes dela.
        foreach (var scene in batch.Scenes)
        {
            sb.Append('\u001e').Append(scene.Key).Append('|').Append(scene.Summary).Append('|').Append(string.Join(';', scene.Tone)).Append('|').Append(string.Join(';', scene.Jokes))
                .Append('|').Append(string.Join(';', scene.Relations.Select(r => $"{r.Between}={r.Note}")));
        }

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

    /// <summary>
    /// Lê o cache. Arquivo vazio ou fora do formato recusa com <see cref="VNForgeException"/> em português e não toca no arquivo: quem chama não segue com um cache vazio,
    /// que o próximo Save gravaria por cima das traduções já pagas (R7).
    /// </summary>
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
            throw new InvalidFileException(path, "o cache de traduções está vazio ou corrompido. O arquivo não foi alterado; apague-o para refazer o cache, e as traduções já pagas serão cobradas de novo.", e);
        }
    }

    /// <summary>
    /// Como <see cref="Save"/>, mas devolve a falha de disco em vez de lançá-la, para quem grava num <c>finally</c> e não pode esconder a exceção que já está subindo.
    /// Só grava se o cache tem alguma entrada. Devolve <c>null</c> quando gravou ou não havia o que gravar.
    /// </summary>
    public IOException? TrySave(string path)
    {
        if (Count == 0)
            return null;
        try
        {
            Save(path);
            return null;
        }
        catch (IOException e)
        {
            return e;
        }
    }

    /// <summary>Grava o cache por <see cref="AtomicFile"/>: temporário ao lado, troca e releitura, para uma queda ou um disco que zera o rename não deixar o anterior perdido.</summary>
    public void Save(string path)
    {
        SortedDictionary<string, string> snapshot;
        lock (_gate)
            snapshot = new(_entries, StringComparer.Ordinal);

        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(snapshot));
    }
}
