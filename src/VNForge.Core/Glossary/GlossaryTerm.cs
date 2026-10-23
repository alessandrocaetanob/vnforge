using System.Text.Json.Serialization;

namespace VNForge.Core.Glossary;

/// <summary>Um termo fixo de <c>glossary.json</c>: como o original deve ser traduzido no jogo inteiro.</summary>
/// <param name="Source">Termo no idioma original, como aparece no texto extraído. Único no arquivo (comparação ordinal).</param>
/// <param name="Target">Tradução fixa do termo.</param>
/// <param name="Notes">Observação livre para o tradutor e para a IA (contexto, uso); nula quando não há.</param>
public sealed record GlossaryTerm(
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null) : IGlossaryEntry;
