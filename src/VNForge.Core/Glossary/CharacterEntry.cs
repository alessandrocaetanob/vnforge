using System.Text.Json.Serialization;
using VNForge.Core.Vndb;

namespace VNForge.Core.Glossary;

/// <summary>Um personagem de <c>characters.json</c>: o nome original (o <see cref="Text.TextUnit.Speaker"/> extraído), a tradução e o que a IA precisa para falar como ele.</summary>
/// <param name="Source">Nome no idioma original, exatamente como a engine o extrai em <see cref="Text.TextUnit.Speaker"/>. Único no arquivo (comparação ordinal).</param>
/// <param name="Target">Nome traduzido.</param>
/// <param name="Gender">Gênero do personagem, para concordância na tradução; <see cref="CharacterGender.Unknown"/> quando não definido.</param>
/// <param name="Tone">Tom de fala em texto livre (por exemplo "formal, usa honoríficos"); nulo quando não definido.</param>
/// <param name="Color">Id da cor do personagem na paleta do app (por exemplo <c>blue</c>), que marca o nome dele na grade, na caixa do jogo e no Modo revisão; nulo quando o app escolhe. O Core só guarda o id.</param>
/// <param name="Romaji">Nome em letras latinas (o nome do VNDB, na ordem ocidental: "Kenta Aizawa"); nulo quando não se sabe.</param>
/// <param name="Aliases">Apelidos e outras formas de chamar o personagem, como o VNDB os lista; nulo quando não há.</param>
/// <param name="Description">Quem é o personagem, em texto corrido e sem a marcação do VNDB; nulo quando não há.</param>
/// <param name="VndbId">Id do personagem no VNDB (<c>c1234</c>), preenchido quando a bíblia usa os personagens do VNDB (<see cref="VndbProjectLinker.ApplyCharacters"/>); nulo quando não se sabe.</param>
/// <param name="Image">A imagem do personagem no VNDB (endereço e marcas de conteúdo; <see cref="VndbImageCache"/> a baixa e a privacidade da Biblioteca decide se aparece); nula quando o VNDB não tem.</param>
public sealed record CharacterEntry(
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Target,
    CharacterGender Gender = CharacterGender.Unknown,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tone = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Romaji = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Aliases = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VndbId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VndbImageRef? Image = null) : IGlossaryEntry;
