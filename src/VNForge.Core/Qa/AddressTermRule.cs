using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Fala que foge do tratamento combinado entre os personagens (#470): o falante tem em <c>address-terms.json</c> um tratamento para alguém
/// (<c>美咲ちゃん</c> vira "Misaki-chan"), o original da fala traz esse tratamento e a tradução não traz o combinado. Só avisa, e o aviso
/// não trava o build (<see cref="QaReport.BlockingIssues"/>): a pessoa decide se a fala muda ou se a exceção é de propósito.
/// </summary>
public sealed class AddressTermRule : IQaRule
{
    public const string RuleId = "tratamento-fora-do-combinado";

    /// <inheritdoc />
    public string Id => RuleId;

    /// <inheritdoc />
    public string Description => "Fala que não usa o tratamento combinado entre os personagens (address-terms.json).";

    /// <inheritdoc />
    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.AddressTerms.Count == 0 ? [] : CheckAll(context);
    }

    private static IEnumerable<QaIssue> CheckAll(QaContext context)
    {
        var book = new AddressBook(context.AddressTerms.Where(IsUsable));
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                if (!unit.HasTranslation)
                {
                    continue;
                }

                foreach (var message in CheckUnit(unit, book))
                {
                    yield return new QaIssue(RuleId, QaSeverity.Warning, unit.Id, key, message);
                }
            }
        }
    }

    // Sem o original ou sem a tradução do tratamento não há o que conferir: a linha da tabela ainda está sendo preenchida.
    private static bool IsUsable(AddressTerm term) =>
        !string.IsNullOrWhiteSpace(term.Source) && !string.IsNullOrWhiteSpace(term.Target);

    private static IEnumerable<string> CheckUnit(TextUnit unit, AddressBook book)
    {
        // O mesmo tratamento no original pode servir a mais de um par (先輩 para dois veteranos): basta a tradução trazer um dos combinados.
        foreach (var group in book.From(unit.EffectiveSpeaker).GroupBy(t => t.Source.Trim(), StringComparer.Ordinal))
        {
            if (!unit.Source.Contains(group.Key, StringComparison.Ordinal)
                || group.Any(t => (unit.Translation ?? string.Empty).Contains(t.Target.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var agreed = string.Join(" ou ", group.Select(t => $"“{t.Target.Trim()}”").Distinct(StringComparer.Ordinal));
            var to = string.Join(" ou ", group.Select(t => t.To.Trim()).Distinct(StringComparer.Ordinal));
            yield return $"{unit.EffectiveSpeaker!.Trim()} chama {to} de “{group.Key}” no original, e o tratamento combinado é {agreed}; a tradução não o usa.";
        }
    }
}
