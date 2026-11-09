using System.Collections.Concurrent;
using VNForge.Core.Bible;

namespace VNForge.AI.Bible;

/// <summary>
/// Provedor determinístico para testes e para o app sem chave: sem rede e sem SDK. Por padrão dá a cada cena o nome <c>Cena &lt;rótulo&gt;</c>, o resumo com a primeira fala
/// e os falantes como "quem aparece"; a sinopse junta o nome dos capítulos.
/// </summary>
public sealed class FakeBibleProvider : IBibleProvider
{
    private readonly Func<BibleChapterInput, string, BibleScene> _describe;
    private readonly ConcurrentQueue<(BibleChapterInput Chapter, IReadOnlyList<string> Keys)> _sceneCalls = new();
    private readonly ConcurrentQueue<IReadOnlyList<BibleChapterDigest>> _overviewCalls = new();

    /// <param name="describe">Descreve uma cena do capítulo; pode lançar para simular uma falha.</param>
    public FakeBibleProvider(Func<BibleChapterInput, string, BibleScene>? describe = null)
    {
        _describe = describe ?? DefaultDescribe;
    }

    /// <summary>As chamadas de leitura de cenas, na ordem.</summary>
    public IReadOnlyCollection<(BibleChapterInput Chapter, IReadOnlyList<string> Keys)> SceneCalls => _sceneCalls;

    /// <summary>Os resumos recebidos em cada pedido de sinopse, na ordem.</summary>
    public IReadOnlyCollection<IReadOnlyList<BibleChapterDigest>> OverviewCalls => _overviewCalls;

    public Task<IReadOnlyList<BibleScene>> DescribeScenesAsync(BibleChapterInput chapter, IReadOnlyList<string> sceneKeys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(sceneKeys);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<BibleScene>>(ct);
        }

        _sceneCalls.Enqueue((chapter, sceneKeys));
        try
        {
            IReadOnlyList<BibleScene> scenes = [.. sceneKeys.Select(k => _describe(chapter, k))];
            return Task.FromResult(scenes);
        }
        catch (Exception e)
        {
            return Task.FromException<IReadOnlyList<BibleScene>>(e);
        }
    }

    public Task<BibleOverview> SummarizeGameAsync(IReadOnlyList<BibleChapterDigest> chapters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<BibleOverview>(ct);
        }

        _overviewCalls.Enqueue(chapters);
        return Task.FromResult(new BibleOverview(
            $"Sinopse de teste: {string.Join(", ", chapters.Select(c => c.Name))}.",
            [.. chapters.Select(c => new BibleTimelineEntry(c.Name, $"{c.Summaries.Count} cenas"))]));
    }

    private static BibleScene DefaultDescribe(BibleChapterInput chapter, string key)
    {
        var scene = chapter.Scenes.Single(s => s.Key == key);
        var first = scene.Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Source))?.Source ?? string.Empty;
        return new BibleScene(key)
        {
            Name = $"Cena {(scene.Label.Length > 0 ? scene.Label : key)}",
            Summary = $"Resumo de teste de {key}: {first}",
            Characters = [.. scene.Lines.Select(l => l.Speaker).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct(StringComparer.Ordinal)],
            Tone = ["neutro"],
        };
    }
}
