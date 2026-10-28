namespace VNForge.Core.Qa;

/// <summary>
/// Regra entre linhas: o mesmo original traduzido de jeitos diferentes no projeto inteiro. Dentro de cada grupo de originais iguais
/// (<see cref="ConsistencyGroups.Find"/>), a tradução mais usada (no empate, a que aparece primeiro) é a referência e cada linha que difere dela recebe
/// um aviso. Linhas sem tradução utilizável não entram, e um grupo que o usuário mandou manter diferente (<see cref="ConsistencyFlags.Keep"/>) não
/// avisa até o original ou uma das traduções mudar. A Conferência e <c>vnf check</c> montam a visão por original com os mesmos grupos.
/// </summary>
public sealed class InconsistentTranslationRule : IQaRule
{
    public const string RuleId = "traducao-inconsistente";

    public string Id => RuleId;

    public string Description => "Mesmo original traduzido de jeitos diferentes.";

    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckCore(context);
    }

    private IEnumerable<QaIssue> CheckCore(QaContext context)
    {
        foreach (var group in ConsistencyGroups.Find(context))
        {
            if (group.IsKept)
            {
                continue;
            }

            foreach (var variant in group.Variants.Where(v => !v.IsReference))
            {
                foreach (var line in variant.Lines)
                {
                    yield return new QaIssue(Id, QaSeverity.Warning, line.UnitId, line.ScriptKey,
                        $"o original “{group.Source}” aparece traduzido como “{group.Reference.Translation}” em outras linhas");
                }
            }
        }
    }
}
