using VNForge.Core.Glossary;

namespace VNForge.AI.Glossary;

/// <summary>Um personagem e a amostra das falas dele (<see cref="SpeakerSamples"/>), o que a IA lê para sugerir voz e gênero.</summary>
/// <param name="Name">O nome original, como a engine o extrai em <c>Speaker</c>.</param>
public sealed record CharacterVoiceRequest(string Name, IReadOnlyList<string> Lines);

/// <summary>O gênero e o jeito de falar sugeridos para um personagem.</summary>
/// <param name="Voice">Como o personagem fala, em uma frase curta, em português ("animada, trata o veterano com respeito").</param>
public sealed record CharacterVoiceSuggestion(string Name, CharacterGender Gender, string Voice);

/// <summary>Um termo que se repete (<see cref="TermCandidate"/>) para a IA traduzir, com uma linha de exemplo para dar contexto.</summary>
public sealed record TermTranslationRequest(string Source, int Lines, string? Example = null);

/// <summary>A tradução sugerida de um termo e o motivo, em uma frase.</summary>
public sealed record TermTranslationSuggestion(string Source, string Target, string Reason);

/// <summary>
/// Fronteira entre a página Glossário e a IA: sugere voz e gênero dos personagens e a tradução dos termos que se repetem. O provedor
/// Claude e o <see cref="FakeGlossarySuggester"/> dos testes a implementam. Só sugere: quem aplica é a pessoa.
/// </summary>
public interface IGlossarySuggester
{
    /// <summary>Uma sugestão por personagem pedido, na ordem do pedido.</summary>
    Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct);

    /// <summary>Uma sugestão por termo pedido, na ordem do pedido.</summary>
    Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct);
}
