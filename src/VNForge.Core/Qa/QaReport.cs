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

    /// <summary>
    /// As ocorrências que o build pode recusar: erros e avisos, sem as linhas sem tradução e sem os avisos de estilo
    /// (<see cref="HonorificsRule"/>, <see cref="GenderRule"/> e <see cref="AddressTermRule"/>), que são sugestões de revisão. Sem essa exceção, um projeto antigo com
    /// personagens sem gênero deixaria de compilar. É a lista que a mensagem de bloqueio, o Build e o Painel contam.
    /// </summary>
    public IReadOnlyList<QaIssue> BlockingIssues => [.. Issues.Where(i => !IsUntranslated(i) && !IsStyleAdvice(i))];

    /// <summary>Quantos avisos de estilo (<see cref="HonorificsRule"/> e <see cref="GenderRule"/>) há; aparecem na Conferência e no <c>vnf check</c>, mas não travam nada.</summary>
    public int StyleAdviceCount => Issues.Count(IsStyleAdvice);

    /// <summary>Verdadeiro quando o <c>vnf build</c> deve parar: há erro, ou há aviso em <see cref="BlockingIssues"/> e o <paramref name="allowWarnings"/> é falso.</summary>
    public bool Blocks(bool allowWarnings) => Issues.Any(i => i.Severity == QaSeverity.Error) || (!allowWarnings && BlockingIssues.Count > 0);

    private static bool IsUntranslated(QaIssue issue) => issue.RuleId == UntranslatedQaRule.RuleId;

    private static bool IsStyleAdvice(QaIssue issue) => issue.RuleId is HonorificsRule.RuleId or GenderRule.RuleId or AddressTermRule.RuleId;
}
