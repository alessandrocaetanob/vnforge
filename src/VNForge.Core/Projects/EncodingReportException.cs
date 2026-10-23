using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>O build foi recusado porque a estratégia de codificação não representa algum caractere das traduções. Carrega o relatório inteiro.</summary>
public sealed class EncodingReportException : VNForgeException
{
    private const int MaxLinesInMessage = 20;

    public EncodingReportException(EncodingReport report)
        : base(BuildMessage(report))
    {
        Report = report;
    }

    /// <summary>Todos os casos, não só os que a mensagem lista.</summary>
    public EncodingReport Report { get; }

    private static string BuildMessage(EncodingReport report)
    {
        var blocking = report.Blocking;
        var lines = blocking.Count == 1 ? "1 caractere" : $"{blocking.Count} caracteres";
        var message = new System.Text.StringBuilder(
            $"A estratégia de codificação '{report.Strategy}' não representa {lines} da tradução; nenhum arquivo foi gravado:");
        foreach (var issue in blocking.Take(MaxLinesInMessage))
        {
            message.Append($"\n  {issue.UnitId} posição {issue.Position}: '{issue.Character}' ({issue.CodePoint})");
        }

        if (blocking.Count > MaxLinesInMessage)
        {
            message.Append($"\n  ... e mais {blocking.Count - MaxLinesInMessage}.");
        }

        message.Append("\nRode vnf build --report para ver a lista completa ou troque o encoding.strategy do vnforge.json.");
        return message.ToString();
    }
}
