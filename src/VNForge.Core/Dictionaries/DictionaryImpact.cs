using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Dictionaries;

/// <summary>Em que texto a regra troca: o original (pré-dicionário) ou a tradução (pós-dicionário).</summary>
public enum DictionarySide
{
    Pre,
    Post,
}

/// <summary>Uma linha em que a regra troca ou em que a condição impede a troca.</summary>
/// <param name="Unit">A linha.</param>
/// <param name="Before">O texto antes da regra (o original no pré, a tradução no pós).</param>
/// <param name="After">O texto que a regra deixaria; igual a <paramref name="Before"/> não existe, porque só entra a linha que mudaria.</param>
public sealed record DictionarySample(TextUnit Unit, string Before, string After);

/// <summary>
/// O alcance de uma regra no projeto: em quantas linhas ela troca e em quantas o padrão aparece mas a condição impede a troca, com uma
/// linha de exemplo de cada. Conta linhas, não ocorrências.
/// </summary>
/// <param name="Applies">Linhas em que o padrão aparece e todas as condições batem.</param>
/// <param name="Blocked">Linhas em que o padrão aparece e alguma condição não bate.</param>
/// <param name="FirstApplied">A primeira linha em que a regra troca; nula quando não há.</param>
/// <param name="FirstBlocked">A primeira linha em que a condição impede a troca; nula quando não há.</param>
public sealed record DictionaryImpact(int Applies, int Blocked, DictionarySample? FirstApplied, DictionarySample? FirstBlocked)
{
    /// <summary>Conta o alcance de <paramref name="rule"/> em <paramref name="units"/>. A regra desligada também é contada: o número diz o que ela faria.</summary>
    /// <param name="kinds">Os papéis que o build formata (<see cref="FormattingProfile.Kinds"/>). No pós só contam as linhas que o build grava (<see cref="TextUnit.NeedsRewrite"/>) desses papéis; nulo conta todo papel.</param>
    /// <exception cref="FormattingException">O padrão é vazio, a regex é inválida ou passou do tempo limite.</exception>
    public static DictionaryImpact Of(DictionaryRule rule, DictionarySide side, IEnumerable<TextUnit> units, DictionaryContext? context = null, IReadOnlyCollection<TextUnitKind>? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(units);
        context ??= DictionaryContext.Empty;

        var bare = new DictionaryStage("dictionary-impact", [rule with { Enabled = true, Conditions = [] }], "$.rule");
        var applies = 0;
        var blocked = 0;
        DictionarySample? firstApplied = null;
        DictionarySample? firstBlocked = null;
        foreach (var unit in units)
        {
            if (side == DictionarySide.Post && (!unit.NeedsRewrite || (kinds is not null && !kinds.Contains(unit.Kind))))
            {
                continue;
            }

            var text = side == DictionarySide.Pre ? unit.Source : unit.Translation;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            string after;
            try
            {
                after = bare.Apply(text, new FormattingContext());
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (string.Equals(after, text, StringComparison.Ordinal))
            {
                continue;
            }

            var sample = new DictionarySample(unit, text, after);
            if (rule.Conditions.Count == 0 || context.Line(unit).Matches(rule.Conditions))
            {
                applies++;
                firstApplied ??= sample;
            }
            else
            {
                blocked++;
                firstBlocked ??= sample;
            }
        }

        return new DictionaryImpact(applies, blocked, firstApplied, firstBlocked);
    }
}
