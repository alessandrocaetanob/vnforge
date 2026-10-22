using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>
/// Papel de uma <see cref="TextUnit"/> no jogo. Os valores gravados no JSON são os do doc 02 e não mudam:
/// os arquivos <c>text/*.json</c> do projeto dependem deles.
/// </summary>
[JsonConverter(typeof(StrictEnumConverter<TextUnitKind>))]
public enum TextUnitKind
{
    /// <summary>Fala de um personagem.</summary>
    [JsonStringEnumMemberName("dialogue")]
    Dialogue,

    /// <summary>Narração, sem falante.</summary>
    [JsonStringEnumMemberName("narration")]
    Narration,

    /// <summary>Nome de personagem ou de lugar mostrado na tela.</summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>Opção de uma escolha do jogador.</summary>
    [JsonStringEnumMemberName("choice")]
    Choice,

    /// <summary>Texto de interface e de sistema (menus, avisos).</summary>
    [JsonStringEnumMemberName("system")]
    System,

    /// <summary>Qualquer outro texto traduzível que a engine encontrou (ex.: strings de expressão com japonês).</summary>
    [JsonStringEnumMemberName("other")]
    Other,
}
