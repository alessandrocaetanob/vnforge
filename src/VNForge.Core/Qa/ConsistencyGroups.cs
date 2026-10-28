using System.Security.Cryptography;
using System.Text;

namespace VNForge.Core.Qa;

/// <summary>Uma linha de uma variante: o id da unidade e o script dela.</summary>
public sealed record ConsistencyLine(string UnitId, string ScriptKey);

/// <summary>
/// Uma tradução que o grupo tem, com as linhas que a usam. <see cref="Diff"/> compara o texto com o da referência
/// (<see cref="TextDiff.Compare"/>); na própria referência é um trecho <see cref="TextDiffKind.Same"/> só.
/// </summary>
/// <param name="Translation">O texto, sem os espaços das pontas.</param>
/// <param name="Lines">As linhas com esta tradução, na ordem do projeto.</param>
/// <param name="Diff">A diferença para a referência.</param>
/// <param name="IsReference">Verdadeiro na tradução mais usada do grupo.</param>
public sealed record ConsistencyVariant(string Translation, IReadOnlyList<ConsistencyLine> Lines, IReadOnlyList<TextDiffSegment> Diff, bool IsReference);

/// <summary>
/// Um original com duas ou mais traduções diferentes no projeto. As variantes vêm da mais usada (a <see cref="Reference"/>) para a menos usada; no
/// empate, a que aparece primeiro no projeto vem antes. É o que a Conferência mostra num cartão só e o que <c>vnf check</c> lista.
/// </summary>
/// <param name="Source">O original, sem os espaços das pontas.</param>
/// <param name="Variants">As traduções, a referência primeiro.</param>
/// <param name="IsKept">Verdadeiro quando o usuário mandou manter as traduções diferentes (<see cref="ConsistencyFlags.Keep"/>) e nada mudou desde então.</param>
public sealed record ConsistencyGroup(string Source, IReadOnlyList<ConsistencyVariant> Variants, bool IsKept)
{
    /// <summary>A tradução mais usada.</summary>
    public ConsistencyVariant Reference => Variants[0];

    /// <summary>Quantas linhas o grupo tem, somando as variantes.</summary>
    public int LineCount => Variants.Sum(v => v.Lines.Count);

    /// <summary>Todas as linhas do grupo, a referência primeiro.</summary>
    public IEnumerable<ConsistencyLine> Lines => Variants.SelectMany(v => v.Lines);

    /// <summary>
    /// Identifica o grupo como ele está agora: o original, cada tradução e as linhas de cada uma. Muda se o original, uma tradução ou a qual variante
    /// uma linha pertence mudar, e por isso "manter diferentes" (<see cref="ConsistencyFlags.Keep"/>) deixa de valer nesse caso.
    /// </summary>
    public string Fingerprint { get; } = Compute(Source, Variants);

    private static string Compute(string source, IReadOnlyList<ConsistencyVariant> variants)
    {
        var text = new StringBuilder(source).Append('\0');
        foreach (var variant in variants.OrderBy(v => v.Translation, StringComparer.Ordinal))
        {
            text.Append(variant.Translation).Append('\u0001');
            text.AppendJoin(',', variant.Lines.Select(l => l.UnitId).Order(StringComparer.Ordinal)).Append('\u0002');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).AsSpan(0, 8));
    }
}

/// <summary>Acha os <see cref="ConsistencyGroup"/> do projeto. É o que a regra <see cref="InconsistentTranslationRule"/> aponta, mas inteiro, com todas as linhas.</summary>
public static class ConsistencyGroups
{
    /// <summary>O prefixo da marca que "manter diferentes de propósito" grava em <see cref="Text.TextUnit.Flags"/>: <c>traducao-inconsistente:&lt;impressão&gt;</c>.</summary>
    public const string FlagPrefix = InconsistentTranslationRule.RuleId + ":";

    /// <summary>A marca que mantém o <paramref name="group"/> como está agora.</summary>
    public static string FlagFor(ConsistencyGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return FlagPrefix + group.Fingerprint;
    }

    /// <summary>
    /// Os originais com mais de uma tradução, na ordem em que o original aparece primeiro. Linhas sem tradução utilizável e originais vazios não entram;
    /// espaços nas pontas do original e da tradução não contam como diferença.
    /// </summary>
    public static IReadOnlyList<ConsistencyGroup> Find(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var order = new List<string>();
        var bySource = new Dictionary<string, List<(ConsistencyLine Line, string Translation, IReadOnlyList<string> Flags)>>(StringComparer.Ordinal);
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                var source = unit.Source.Trim();
                if (source.Length == 0 || !unit.HasTranslation || unit.Translation is not { } translation)
                {
                    continue;
                }

                if (!bySource.TryGetValue(source, out var list))
                {
                    bySource[source] = list = [];
                    order.Add(source);
                }

                list.Add((new ConsistencyLine(unit.Id, key), translation.Trim(), unit.Flags));
            }
        }

        var groups = new List<ConsistencyGroup>();
        foreach (var source in order)
        {
            var list = bySource[source];
            var byTranslation = list.GroupBy(x => x.Translation, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ToList();
            if (byTranslation.Count < 2)
            {
                continue;
            }

            var reference = byTranslation[0].Key;
            var variants = byTranslation
                .Select((g, index) => new ConsistencyVariant(g.Key, [.. g.Select(x => x.Line)], TextDiff.Compare(reference, g.Key), IsReference: index == 0))
                .ToList();
            var group = new ConsistencyGroup(source, variants, IsKept: false);
            var flag = FlagFor(group);
            groups.Add(list.All(x => x.Flags.Contains(flag, StringComparer.Ordinal)) ? group with { IsKept = true } : group);
        }

        return groups;
    }
}
