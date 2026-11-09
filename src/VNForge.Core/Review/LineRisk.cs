using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Review;

/// <summary>O quanto uma linha precisa da pessoa: a faixa da pontuação de <see cref="LineRisk"/>.</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>O que pôs pontos na pontuação de uma linha.</summary>
public enum RiskFactorKind
{
    /// <summary>A IA registrou uma dúvida ao traduzir (<see cref="TextUnit.Doubt"/>).</summary>
    AiDoubt,

    /// <summary>O QA achou um problema na linha.</summary>
    QaIssue,

    /// <summary>Quem leu a cena marcou a fala como "soa estranho" (<see cref="ReadingFlags"/>).</summary>
    SoundsOdd,

    /// <summary>A passada de editor propôs um ajuste que ainda vale para a linha.</summary>
    EditorSuggestion,

    /// <summary>A tradução da linha não é a que a IA escreveu: duas passadas discordam sobre o texto.</summary>
    PassDivergence,
}

/// <summary>Os pontos de cada sinal e as faixas de <see cref="RiskLevel"/>. Somados chegam a 108; a pontuação para em <see cref="MaxScore"/>.</summary>
public static class RiskWeights
{
    public const int AiDoubt = 30;

    public const int QaError = 40;

    public const int QaWarning = 24;

    /// <summary>As sugestões de estilo do QA (honorífico, gênero, ortografia, tratamento) pesam menos que um aviso: não travam o build.</summary>
    public const int QaStyleAdvice = 10;

    public const int SoundsOdd = 20;

    public const int EditorSuggestion = 10;

    public const int PassDivergence = 8;

    public const int MaxScore = 100;

    /// <summary>A partir daqui a linha é de risco médio.</summary>
    public const int MediumFrom = 30;

    /// <summary>A partir daqui a linha é de risco alto.</summary>
    public const int HighFrom = 60;

    public static RiskLevel LevelOf(int score) => score switch
    {
        >= HighFrom => RiskLevel.High,
        >= MediumFrom => RiskLevel.Medium,
        _ => RiskLevel.Low,
    };
}

/// <summary>Um sinal que pesou na pontuação: o que é, quantos pontos deu e o texto do chip.</summary>
/// <param name="Label">O nome do sinal em português, sem os pontos ("Dúvida da IA", "Aviso do QA: passa 1 linha").</param>
public sealed record RiskFactor(RiskFactorKind Kind, int Points, string Label)
{
    /// <summary>O texto do chip, com os pontos: "Dúvida da IA +30".</summary>
    public string Text => $"{Label} +{Points}";

    /// <summary>O nome curto para a coluna "Sinais" de uma lista: o do QA perde a mensagem da regra ("QA").</summary>
    public string ShortLabel => Kind == RiskFactorKind.QaIssue ? "QA" : Label;
}

/// <summary>
/// A pontuação de risco de uma linha, de 0 a 100: a soma dos sinais que pedem a atenção da pessoa (dúvida da IA, QA, "soa estranho", sugestão do editor e
/// divergência entre passadas). Só lê a linha; não grava nada.
/// </summary>
/// <param name="Line">A linha pontuada, com os avisos do QA que a atingem.</param>
public sealed record LineRisk(LineEntry Line, int Score, RiskLevel Level, IReadOnlyList<RiskFactor> Factors)
{
    /// <param name="hasEditorSuggestion">A passada de editor tem um ajuste que ainda vale para a linha (<see cref="PendingEditorSuggestions.Covers"/>).</param>
    public static LineRisk Of(LineEntry line, bool hasEditorSuggestion = false)
    {
        ArgumentNullException.ThrowIfNull(line);
        var unit = line.Unit;
        var factors = new List<RiskFactor>(5);
        if (unit.Doubt is not null)
        {
            factors.Add(new RiskFactor(RiskFactorKind.AiDoubt, RiskWeights.AiDoubt, "Dúvida da IA"));
        }

        if (QaFactor(line.Issues) is { } qa)
        {
            factors.Add(qa);
        }

        if (unit.IsOdd())
        {
            factors.Add(new RiskFactor(RiskFactorKind.SoundsOdd, RiskWeights.SoundsOdd, "Soa estranho"));
        }

        if (hasEditorSuggestion)
        {
            factors.Add(new RiskFactor(RiskFactorKind.EditorSuggestion, RiskWeights.EditorSuggestion, "Sugestão do editor"));
        }

        if (unit.AiTranslation is { } ai && !string.Equals(ai, unit.Translation, StringComparison.Ordinal))
        {
            factors.Add(new RiskFactor(RiskFactorKind.PassDivergence, RiskWeights.PassDivergence, "Passadas divergem"));
        }

        var score = Math.Min(RiskWeights.MaxScore, factors.Sum(f => f.Points));
        return new LineRisk(line, score, RiskWeights.LevelOf(score), factors);
    }

    /// <summary>O pior problema do QA vale uma vez: três avisos na mesma linha pesam como um.</summary>
    private static RiskFactor? QaFactor(IReadOnlyList<QaIssue> issues)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        var worst = issues.FirstOrDefault(i => i.Severity == QaSeverity.Error);
        if (worst is not null)
        {
            return new RiskFactor(RiskFactorKind.QaIssue, RiskWeights.QaError, $"Erro do QA: {worst.Message}");
        }

        worst = issues.FirstOrDefault(i => !QaReport.IsStyleAdvice(i));
        return worst is not null
            ? new RiskFactor(RiskFactorKind.QaIssue, RiskWeights.QaWarning, $"Aviso do QA: {worst.Message}")
            : new RiskFactor(RiskFactorKind.QaIssue, RiskWeights.QaStyleAdvice, $"Sugestão de estilo do QA: {issues[0].Message}");
    }
}
