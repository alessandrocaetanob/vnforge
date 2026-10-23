using System.Text.Json.Serialization;

namespace VNForge.Core.Glossary;

/// <summary>Um personagem de <c>characters.json</c>: o nome original (o <see cref="Text.TextUnit.Speaker"/> extraído), a tradução e o que a IA precisa para falar como ele.</summary>
/// <param name="Source">Nome no idioma original, exatamente como a engine o extrai em <see cref="Text.TextUnit.Speaker"/>. Único no arquivo (comparação ordinal).</param>
/// <param name="Target">Nome traduzido.</param>
/// <param name="Gender">Gênero do personagem, para concordância na tradução; <see cref="CharacterGender.Unknown"/> quando não definido.</param>
/// <param name="Tone">Tom de fala em texto livre (por exemplo "formal, usa honoríficos"); nulo quando não definido.</param>
public sealed record CharacterEntry(
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Target,
    CharacterGender Gender = CharacterGender.Unknown,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tone = null) : IGlossaryEntry;
