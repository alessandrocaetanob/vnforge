using System.Text;

namespace VNForge.Core.Qa;

/// <summary>O build foi recusado por erros de QA (ou por avisos, sem <c>--allow-warnings</c>). A CLI a converte no código de saída 2.</summary>
public sealed class QaBlockedException : VNForgeException
{
    private const int MaxLinesInMessage = 20;

    public QaBlockedException(QaReport report, bool allowWarnings)
        : base(BuildMessage(report, allowWarnings))
    {
        Report = report;
    }

    /// <summary>Todas as ocorrências, não só as que a mensagem lista.</summary>
    public QaReport Report { get; }

    private static string BuildMessage(QaReport report, bool allowWarnings)
    {
        var blocking = report.Issues.Where(i => i.Severity == QaSeverity.Error || !allowWarnings).ToList();
        var message = new StringBuilder($"O QA bloqueou o build ({Describe(report)}); nenhum arquivo foi gravado:");
        foreach (var issue in blocking.Take(MaxLinesInMessage))
        {
            message.Append($"\n  {Label(issue.Severity)} {issue.UnitId} [{issue.RuleId}] {issue.Message}");
        }

        if (blocking.Count > MaxLinesInMessage)
        {
            message.Append($"\n  ... e mais {blocking.Count - MaxLinesInMessage}.");
        }

        message.Append("\nCorrija a tradução, marque o aviso como ok na linha ou rode com --allow-warnings para liberar só os avisos.");
        return message.ToString();
    }

    /// <summary>Rótulo de uma ocorrência na saída de texto.</summary>
    public static string Label(QaSeverity severity) => severity == QaSeverity.Error ? "ERRO " : "AVISO";

    /// <summary>Resumo "2 erros, 37 avisos".</summary>
    public static string Describe(QaReport report)
    {
        var errors = report.Errors.Count;
        var warnings = report.Warnings.Count;
        return $"{errors} {(errors == 1 ? "erro" : "erros")}, {warnings} {(warnings == 1 ? "aviso" : "avisos")}";
    }
}
