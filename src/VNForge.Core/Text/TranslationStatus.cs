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

    /// <summary>
    /// "Não traduzir": a linha fica como no original de propósito (nome de fonte, código, marca). O build copia o original byte a byte, como em
    /// <see cref="Untranslated"/>, mas a linha conta como pronta e fica fora do QA de completude e dos pedidos à IA. Vem depois de
    /// <see cref="Approved"/> para não mudar os valores numéricos dos outros; não é uma etapa do fluxo, então <c>status &gt;= Reviewed</c> não a significa.
    /// </summary>
    [JsonStringEnumMemberName("keep")]
    Keep,
}
