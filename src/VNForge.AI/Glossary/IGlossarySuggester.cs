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

/// <summary>Um falante e as falas em que ele chama alguém (<see cref="AddressSamples"/>), o que a IA lê para sugerir os tratamentos dele.</summary>
/// <param name="Speaker">O nome original do falante, como está em <c>characters.json</c>.</param>
public sealed record AddressTermRequest(string Speaker, IReadOnlyList<AddressLine> Lines);

/// <summary>Um personagem que pode ser o "com quem fala" de um tratamento, com o nome traduzido quando há.</summary>
public sealed record AddressTermCharacter(string Name, string? Translation = null);

/// <summary>O tratamento sugerido para um par: como <paramref name="From"/> chama <paramref name="To"/> no original e na tradução.</summary>
public sealed record AddressTermSuggestion(string From, string To, string Source, string Target);

/// <summary>
/// Fronteira entre a página Glossário e a IA: sugere voz e gênero dos personagens, a tradução dos termos que se repetem e o tratamento entre personagens. O provedor
/// Claude e o <see cref="FakeGlossarySuggester"/> dos testes a implementam. Só sugere: quem aplica é a pessoa.
/// </summary>
public interface IGlossarySuggester
{
    /// <summary>Uma sugestão por personagem pedido, na ordem do pedido.</summary>
    Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct);

    /// <summary>Uma sugestão por termo pedido, na ordem do pedido.</summary>
    Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct);

    /// <summary>
    /// Os tratamentos que a IA achou nas falas de <paramref name="speakers"/>: no máximo um por par, só entre os <paramref name="characters"/>, sem o falante
    /// consigo mesmo. Um falante sem tratamento claro simplesmente não aparece.
    /// </summary>
    Task<IReadOnlyList<AddressTermSuggestion>> SuggestAddressTermsAsync(IReadOnlyList<AddressTermRequest> speakers, IReadOnlyList<AddressTermCharacter> characters, CancellationToken ct);
}
