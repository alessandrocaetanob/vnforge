using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>De onde veio a tradução de uma <see cref="TextUnit"/>.</summary>
/// <param name="Provider">Quem produziu o texto (ex.: <c>"anthropic"</c>, ou <c>"manual"</c> para uma pessoa).</param>
/// <param name="Model">Modelo usado, quando o provedor é uma IA (ex.: <c>"claude-opus-5-5"</c>); nulo caso contrário.</param>
/// <param name="At">Momento da produção. No JSON vai sempre em UTC, no formato <c>2026-10-17T18:00:00Z</c>; na leitura, valor sem <c>Z</c> nem deslocamento é recusado.</param>
public sealed record TranslationOrigin(
    [property: JsonRequired] string Provider,
    string? Model,
    [property: JsonRequired] DateTimeOffset At);
