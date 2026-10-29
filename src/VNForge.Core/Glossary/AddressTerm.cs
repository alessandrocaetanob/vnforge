using System.Text.Json.Serialization;

namespace VNForge.Core.Glossary;

/// <summary>
/// Como um personagem chama outro (#470): o tratamento de um par, no japonês e na tradução ("Misaki-chan", "senpai", o sobrenome). Muda a tradução
/// de cena a cena e fica em <c>address-terms.json</c>, uma entrada por par <see cref="From"/> e <see cref="To"/>.
/// </summary>
/// <param name="From">Quem fala, pelo nome original de <see cref="CharacterEntry.Source"/>.</param>
/// <param name="To">Com quem fala, pelo nome original de <see cref="CharacterEntry.Source"/>.</param>
/// <param name="Source">Como <paramref name="From"/> chama <paramref name="To"/> no japonês (por exemplo <c>美咲ちゃん</c> ou <c>先輩</c>).</param>
/// <param name="Target">Como a tradução o chama (por exemplo <c>Misaki-chan</c> ou <c>senpai</c>).</param>
public sealed record AddressTerm(
    [property: JsonRequired] string From,
    [property: JsonRequired] string To,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Target);
