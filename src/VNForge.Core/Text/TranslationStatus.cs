using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>
/// Etapa do fluxo de tradução de uma <see cref="TextUnit"/>, em ordem de progresso (o valor numérico cresce
/// a cada etapa, então <c>status &gt;= Reviewed</c> significa "revisada ou melhor"). Os valores gravados no JSON são os do doc 02.
/// </summary>
[JsonConverter(typeof(StrictEnumConverter<TranslationStatus>))]
public enum TranslationStatus
{
    /// <summary>Sem tradução. O build copia o original.</summary>
    [JsonStringEnumMemberName("untranslated")]
    Untranslated,

    /// <summary>Tradução de IA, ainda sem conferência humana.</summary>
    [JsonStringEnumMemberName("machine")]
    Machine,

    /// <summary>Traduzida por uma pessoa.</summary>
    [JsonStringEnumMemberName("translated")]
    Translated,

    /// <summary>Revisada.</summary>
    [JsonStringEnumMemberName("reviewed")]
    Reviewed,

    /// <summary>Aprovada para o build final.</summary>
    [JsonStringEnumMemberName("approved")]
    Approved,
}
