using VNForge.AI.Claude;

namespace VNForge.AI.Cost;

/// <summary>As três qualidades que a pessoa escolhe no lugar do modelo: Rápida, Equilibrada e Caprichada.</summary>
public enum TranslationQuality
{
    Fast,
    Balanced,
    Careful,
}

/// <summary>
/// Uma qualidade: o modelo que ela usa e o ritmo estimado. <see cref="LinesPerMinute"/> é um chute para dar o tempo da tela
/// (a medição real, por projeto, vem depois): serve para a ordem de grandeza, não para prometer prazo.
/// </summary>
public sealed record QualityProfile(TranslationQuality Quality, string Model, int LinesPerMinute)
{
    /// <summary>O tempo para traduzir <paramref name="lines"/> linhas neste ritmo.</summary>
    public TimeSpan TimeFor(int lines) =>
        lines <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes((double)lines / LinesPerMinute);
}

public static class QualityProfiles
{
    /// <summary>Da mais rápida para a mais cuidadosa.</summary>
    public static IReadOnlyList<QualityProfile> All { get; } =
    [
        new(TranslationQuality.Fast, ClaudeModels.Haiku, 480),
        new(TranslationQuality.Balanced, ClaudeModels.Sonnet, 240),
        new(TranslationQuality.Careful, ClaudeModels.Opus, 160),
    ];

    public static QualityProfile For(TranslationQuality quality) =>
        All.Single(p => p.Quality == quality);

    /// <summary>A qualidade que usa <paramref name="model"/>; <see langword="null"/> para o modelo que só existe nos detalhes técnicos (Fable).</summary>
    public static QualityProfile? ForModel(string model) =>
        All.FirstOrDefault(p => p.Model == model);
}
