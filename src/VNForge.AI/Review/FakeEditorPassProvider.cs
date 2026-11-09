using System.Collections.Concurrent;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>
/// Provedor da passada de editor para testes e para a cena de exemplo: sem rede e sem SDK. Por padrão propõe tirar o espaço duplo de qualquer linha que o tenha
/// (tipo fluidez) e deixa as demais como estão; <c>propose</c> troca a regra.
/// </summary>
public sealed class FakeEditorPassProvider : IEditorPassProvider
{
    private readonly Func<TextUnit, EditorPassProposal?> _propose;
    private readonly ConcurrentQueue<TranslationBatch> _received = new();

    public FakeEditorPassProvider(Func<TextUnit, EditorPassProposal?>? propose = null)
    {
        _propose = propose ?? DoubleSpace;
    }

    /// <summary>Cenas recebidas, na ordem das chamadas.</summary>
    public IReadOnlyCollection<TranslationBatch> ReceivedScenes => _received;

    public Task<IReadOnlyList<EditorPassProposal>> ReviewSceneAsync(TranslationBatch scene, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<EditorPassProposal>>(ct);
        }

        _received.Enqueue(scene);
        IReadOnlyList<EditorPassProposal> proposals = [.. scene.Lines.Select(_propose).OfType<EditorPassProposal>()];
        return Task.FromResult(proposals);
    }

    /// <summary>Por padrão devolve a proposta recusada com "(outra)" no fim; esta função troca a regra.</summary>
    public Func<TextUnit, string, EditorPassProposal?>? Another { get; init; }

    public Task<EditorPassProposal?> AnotherAsync(TranslationBatch line, string rejected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<EditorPassProposal?>(ct);
        }

        var unit = line.Lines[0];
        return Task.FromResult(Another is { } rule
            ? rule(unit, rejected)
            : new EditorPassProposal(unit.Id, EditorSuggestionKind.Fluency, rejected + " (outra)", "Outra forma de dizer."));
    }

    private static EditorPassProposal? DoubleSpace(TextUnit unit) =>
        unit.Translation is { } text && text.Contains("  ", StringComparison.Ordinal)
            ? new EditorPassProposal(unit.Id, EditorSuggestionKind.Fluency, text.Replace("  ", " ", StringComparison.Ordinal), "Tira o espaço duplo.")
            : null;
}
