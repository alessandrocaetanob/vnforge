namespace VNForge.Core.Qa;

/// <summary>Resultado de <see cref="QaEngine.Run"/>.</summary>
/// <param name="Issues">Ocorrências ativas, na ordem das regras e, dentro delas, a que a regra devolveu.</param>
/// <param name="Silenced">Avisos que a linha marcou como "ok" (<see cref="Text.TextUnit.Flags"/>); não contam em <see cref="Issues"/>.</param>
/// <param name="RulesRun">Ids das regras que rodaram, na ordem.</param>
public sealed record QaReport(IReadOnlyList<QaIssue> Issues, IReadOnlyList<QaIssue> Silenced, IReadOnlyList<string> RulesRun)
{
    /// <summary>Relatório de quem não rodou QA.</summary>
    public static QaReport Empty { get; } = new([], [], []);

    /// <summary>Ocorrências de erro.</summary>
    public IReadOnlyList<QaIssue> Errors => [.. Issues.Where(i => i.Severity == QaSeverity.Error)];

    /// <summary>Ocorrências de aviso.</summary>
    public IReadOnlyList<QaIssue> Warnings => [.. Issues.Where(i => i.Severity == QaSeverity.Warning)];

    /// <summary>Verdadeiro quando há erro (nunca silenciável).</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == QaSeverity.Error);

    /// <summary>Linhas sem tradução (<see cref="UntranslatedQaRule"/>): o build copia o original nelas, então só entram na contagem do resumo.</summary>
    public int UntranslatedCount => Issues.Count(IsUntranslated);

    /// <summary>As ocorrências que não são de linha sem tradução; só estas entram no que o build pode bloquear.</summary>
    public IReadOnlyList<QaIssue> OtherIssues => [.. Issues.Where(i => !IsUntranslated(i))];

    /// <summary>Verdadeiro quando o <c>vnf build</c> deve parar: há erro, ou há aviso (que não seja de linha sem tradução) e o <paramref name="allowWarnings"/> é falso.</summary>
    public bool Blocks(bool allowWarnings) => Issues.Any(i => i.Severity == QaSeverity.Error || (!allowWarnings && !IsUntranslated(i)));

    private static bool IsUntranslated(QaIssue issue) => issue.RuleId == UntranslatedQaRule.RuleId;
}
