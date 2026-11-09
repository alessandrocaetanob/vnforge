using VNForge.AI.Translation;
using VNForge.Core.Projects;

namespace VNForge.AI.Review;

/// <summary>
/// A segunda passada de editor (#532): lê a cena já traduzida inteira, de uma vez, e propõe ajustes de fluidez, voz e consistência. É a fronteira entre a passada e a IA:
/// o <see cref="ClaudeEditorPassProvider"/> e o <see cref="FakeEditorPassProvider"/> dos testes a implementam. O provedor só propõe; quem grava o ajuste é a pessoa.
/// </summary>
public interface IEditorPassProvider
{
    /// <summary>
    /// Lê as linhas de <paramref name="scene"/> (cada uma com <c>Translation</c>) e devolve os ajustes, no máximo um por linha e só para as linhas que precisam. Uma cena sem
    /// ajuste volta como lista vazia.
    /// </summary>
    Task<IReadOnlyList<EditorPassProposal>> ReviewSceneAsync(TranslationBatch scene, CancellationToken ct);
}

/// <summary>O ajuste que o modelo propôs para uma linha, antes de a <see cref="EditorPassRunner"/> conferir.</summary>
/// <param name="Id">Id da linha.</param>
/// <param name="Kind">O que o ajuste melhora.</param>
/// <param name="Proposed">A linha inteira, como ficaria.</param>
/// <param name="Reason">O motivo, em uma frase.</param>
public sealed record EditorPassProposal(string Id, EditorSuggestionKind Kind, string Proposed, string Reason);
