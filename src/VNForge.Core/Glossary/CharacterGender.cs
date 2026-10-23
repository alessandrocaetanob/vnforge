using System.Text.Json.Serialization;
using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>Gênero de um personagem em <c>characters.json</c>. Os valores gravados no JSON são os nomes em minúsculas.</summary>
[JsonConverter(typeof(StrictEnumConverter<CharacterGender>))]
public enum CharacterGender
{
    /// <summary>Não definido.</summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>Feminino.</summary>
    [JsonStringEnumMemberName("female")]
    Female,

    /// <summary>Masculino.</summary>
    [JsonStringEnumMemberName("male")]
    Male,

    /// <summary>Outro ou neutro (narrador, grupo, personagem sem gênero).</summary>
    [JsonStringEnumMemberName("other")]
    Other,
}
