using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Fala que foge do tratamento combinado entre os personagens (#470): o falante tem em <c>address-terms.json</c> um tratamento para alguém
/// (<c>美咲ちゃん</c> vira "Misaki-chan"), o original da fala traz esse tratamento e a tradução não traz o combinado. Só avisa, e o aviso
/// não trava o build (<see cref="QaReport.BlockingIssues"/>): a pessoa decide se a fala muda ou se a exceção é de propósito.
/// A conferência é por trecho de texto, sem fronteira de palavra: <c>先輩</c> também casa dentro de <c>田中先輩</c>, e um tratamento curto
/// como "ele" casa dentro de "dele". O aviso é só uma sugestão de revisão, então prefere apontar a mais a deixar passar.
/// </summary>
public sealed class AddressTermRule : UnitQaRule
{
    public const string RuleId = "tratamento-fora-do-combinado";

    /// <inheritdoc />
    public override string Id => RuleId;

    /// <inheritdoc />
    public override string Description => "Fala que não usa o tratamento combinado entre os personagens (address-terms.json).";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    // Sem o original ou sem a tradução do tratamento não há o que conferir: a linha da tabela ainda está sendo preenchida.
    internal static bool IsUsable(AddressTerm term) =>
        !string.IsNullOrWhiteSpace(term.Source) && !string.IsNullOrWhiteSpace(term.Target);

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (!unit.HasTranslation || context.AddressBook.IsEmpty)
        {
            return [];
        }

        var terms = context.AddressBook.From(unit.EffectiveSpeaker);
        return terms.Count == 0 ? [] : Check(unit, terms);
    }

    private static IEnumerable<string> Check(TextUnit unit, IReadOnlyList<AddressTerm> terms)
    {
        // O mesmo tratamento no original pode servir a mais de um par (先輩 para dois veteranos): basta a tradução trazer um dos combinados.
        foreach (var group in terms.GroupBy(t => t.Source.Trim(), StringComparer.Ordinal))
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
