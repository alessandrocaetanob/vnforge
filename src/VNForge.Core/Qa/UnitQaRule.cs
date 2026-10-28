using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>Base das regras que olham uma linha de cada vez, com a gravidade fixa da regra. A regra devolve só as mensagens.</summary>
public abstract class UnitQaRule : IQaRule
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>Gravidade de toda ocorrência desta regra.</summary>
    public abstract QaSeverity Severity { get; }

    /// <inheritdoc />
    public virtual IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckAll(context);
    }

    private IEnumerable<QaIssue> CheckAll(QaContext context)
    {
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                foreach (var message in CheckUnit(unit, context))
                {
                    yield return new QaIssue(Id, Severity, unit.Id, key, message);
                }
            }
        }
    }

    /// <summary>Mensagens de problema da <paramref name="unit"/>; vazio quando está certa.</summary>
    protected abstract IEnumerable<string> CheckUnit(TextUnit unit, QaContext context);
}
