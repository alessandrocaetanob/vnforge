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
    /// <param name="kinds">Os papéis que o build formata (<see cref="FormattingProfile.Kinds"/>). No pós só contam as linhas que o build grava (<see cref="TextUnit.NeedsRewrite"/>) desses papéis; nulo conta qualquer papel.</param>
    /// <exception cref="FormattingException">O padrão é vazio, a regex é inválida ou passou do tempo limite.</exception>
    public static DictionaryImpact Of(DictionaryRule rule, DictionarySide side, IEnumerable<TextUnit> units, DictionaryContext? context = null, IReadOnlyCollection<TextUnitKind>? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(units);
        context ??= DictionaryContext.Empty;

        var bare = new DictionaryStage("dictionary-impact", [rule with { Enabled = true, Conditions = [] }], "$.rule");
        var tally = new Tally();
        foreach (var unit in units)
        {
            if (Sample(bare, side, kinds, unit) is not { } sample)
            {
                continue;
            }

            tally.Add(sample, rule.Conditions.Count == 0 || context.Line(unit).Matches(rule.Conditions));
        }

        return new DictionaryImpact(tally.Applies, tally.Blocked, tally.FirstApplied, tally.FirstBlocked);
    }

    /// <summary>A troca que a regra faria na linha; nula quando a linha não entra na conta, não tem texto ou a regra não a muda.</summary>
    private static DictionarySample? Sample(DictionaryStage bare, DictionarySide side, IReadOnlyCollection<TextUnitKind>? kinds, TextUnit unit)
    {
        if (side == DictionarySide.Post && !IsBuilt(unit, kinds))
        {
            return null;
        }

        var text = side == DictionarySide.Pre ? unit.Source : unit.Translation;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            var after = bare.Apply(text, new FormattingContext());
            return string.Equals(after, text, StringComparison.Ordinal) ? null : new DictionarySample(unit, text, after);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>O build grava a tradução da linha e formata o papel dela.</summary>
    private static bool IsBuilt(TextUnit unit, IReadOnlyCollection<TextUnitKind>? kinds) =>
        unit.NeedsRewrite && (kinds is null || kinds.Contains(unit.Kind));

    private sealed class Tally
    {
        public int Applies { get; private set; }

        public int Blocked { get; private set; }

        public DictionarySample? FirstApplied { get; private set; }

        public DictionarySample? FirstBlocked { get; private set; }

        public void Add(DictionarySample sample, bool applies)
        {
            if (applies)
            {
                Applies++;
                FirstApplied ??= sample;
            }
            else
            {
                Blocked++;
                FirstBlocked ??= sample;
            }
        }
    }
}
