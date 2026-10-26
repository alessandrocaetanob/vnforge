using System.Globalization;
using System.Text.RegularExpressions;

namespace VNForge.Core.Projects;

/// <summary>
/// Um grupo de scripts, o "capítulo" do Painel, com os totais nos três estados que as telas de uso mostram.
/// </summary>
/// <param name="Name">Nome do grupo: o proposto pela pasta ou pela faixa, ou o que a pessoa escolheu.</param>
/// <param name="Number">Posição na história (1, 2, 3...); nulo no grupo <see cref="ScriptGrouping.ExtraName"/>, que não faz parte dela.</param>
/// <param name="ScriptKeys">Chaves dos scripts do grupo, na ordem numérica.</param>
public sealed record ScriptGroupTotals(string Name, int? Number, IReadOnlyList<string> ScriptKeys, int Ready, int ToReview, int Japanese)
{
    public bool IsExtra => Number is null;

    public int Total => Ready + ToReview + Japanese;
}

/// <summary>
/// Agrupa os scripts do projeto em capítulos. O jogo não diz o que é capítulo, então o grupo sai, nesta ordem: do que a pessoa escolheu (<c>group</c> no
/// <c>scripts.json</c>); da pasta do script no jogo, quando o <c>yst_list.ybn</c> a trouxe (<c>userscript/01_cap1</c>) ou, para os scripts soltos na raiz,
/// do prefixo numérico do arquivo (<c>01_prologo</c>); e, sem nada disso, de faixas na ordem numérica dos scripts ("Parte 1", "Parte 2").
/// Pastas sem número no meio de outras com número (<c>system</c>, <c>menu</c>) e scripts sem pasta vão para <see cref="ExtraName"/>, o último grupo, sem número.
/// </summary>
public static partial class ScriptGrouping
{
    /// <summary>Nome do grupo que reúne o que não é a história: menus, nomes, sistema.</summary>
    public const string ExtraName = "Menus e nomes";

    /// <summary>Scripts por faixa quando o jogo não diz nada sobre capítulos.</summary>
    public const int RangeSize = 40;

    /// <summary>No máximo, tantas faixas: um jogo enorme não vira uma lista sem fim.</summary>
    public const int MaxRanges = 12;

    /// <summary>Os grupos do projeto: os da história na ordem em que o primeiro script de cada um aparece, e <see cref="ExtraName"/> por último.</summary>
    public static IReadOnlyList<ScriptGroupTotals> Build(ScriptNames names, IReadOnlyList<FileStatus> files)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(files);
        var counts = files.ToDictionary(f => f.Key, f => f.Counts, StringComparer.Ordinal);
        var keys = counts.Keys.OrderBy(NumberOf).ThenBy(k => k, StringComparer.Ordinal).ToList();
        var assigned = Assign(names, keys);

        var groups = new List<(string Name, List<string> Keys)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var name = assigned[key];
            if (!index.TryGetValue(name, out var at))
            {
                at = groups.Count;
                index[name] = at;
                groups.Add((name, []));
            }

            groups[at].Keys.Add(key);
        }

        var story = groups.Where(g => !IsExtra(g.Name)).ToList();
        var result = new List<ScriptGroupTotals>(groups.Count);
        for (var i = 0; i < story.Count; i++)
        {
            result.Add(Totals(story[i].Name, i + 1, story[i].Keys, counts));
        }

        foreach (var extra in groups.Where(g => IsExtra(g.Name)))
        {
            result.Add(Totals(ExtraName, null, extra.Keys, counts));
        }

        return result;
    }

    /// <summary>Dá um novo nome ao grupo: todos os scripts dele passam a ter o grupo escolhido pela pessoa. Um nome vazio devolve os nomes como estavam.</summary>
    public static ScriptNames RenameGroup(ScriptNames names, IReadOnlyList<FileStatus> files, string groupName, string? newName)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return names;
        }

        var group = Build(names, files).FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        return group is null ? names : group.ScriptKeys.Aggregate(names, (current, key) => current.WithGroup(key, newName));
    }

    /// <summary>Muda os scripts dados para o grupo dado (um grupo que já existe ou um novo); um nome vazio devolve os scripts ao grupo que a pasta propõe.</summary>
    public static ScriptNames Regroup(ScriptNames names, IEnumerable<string> keys, string? groupName)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Aggregate(names, (current, key) => current.WithGroup(key, groupName));
    }

    private static Dictionary<string, string> Assign(ScriptNames names, IReadOnlyList<string> keys)
    {
        var common = CommonFolder(names, keys);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (LabelOf(names, key, common) is { } label)
            {
                labels[key] = label;
            }
        }

        // O que a pasta diz vale para decidir o que fica fora da história, mesmo nos scripts que a pessoa já agrupou.
        var anyNumbered = labels.Values.Any(l => Numbered(l) is not null);
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (names.Group(key) is { } chosen)
            {
                assigned[key] = chosen;
            }
            else if (labels.TryGetValue(key, out var label))
            {
                assigned[key] = Numbered(label) is { } numbered
                    ? ChapterName(numbered.Number, numbered.Remainder)
                    : anyNumbered ? ExtraName : Humanize(label);
            }
        }

        var open = keys.Where(k => !assigned.ContainsKey(k)).ToList();
        if (labels.Count > 0)
        {
            foreach (var key in open)
            {
                assigned[key] = ExtraName;
            }

            return assigned;
        }

        var parts = Math.Clamp((open.Count + RangeSize - 1) / RangeSize, 1, MaxRanges);
        var size = (open.Count + parts - 1) / parts;
        for (var i = 0; i < open.Count; i++)
        {
            assigned[open[i]] = string.Create(CultureInfo.InvariantCulture, $"Parte {(i / size) + 1}");
        }

        return assigned;
    }

    /// <summary>As pastas sem número que todos os scripts com pasta têm em comum no começo (<c>userscript</c>): não distinguem um capítulo do outro. Uma pasta numerada nunca sai, nem num jogo de um capítulo só.</summary>
    private static string[] CommonFolder(ScriptNames names, IReadOnlyList<string> keys)
    {
        string[]? common = null;
        foreach (var key in keys)
        {
            if (names.Folder(key) is not { } folder)
            {
                continue;
            }

            var segments = Segments(folder);
            common = common is null ? segments : [.. common.Zip(segments).TakeWhile(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)).Select(p => p.First)];
        }

        return common is null ? [] : [.. common.TakeWhile(s => !char.IsAsciiDigit(s[0]))];
    }

    private static string? LabelOf(ScriptNames names, string key, string[] common)
    {
        if (names.Folder(key) is { } folder && Segments(folder).Skip(common.Length).FirstOrDefault() is { } segment)
        {
            return segment;
        }

        var name = names.DetectedName(key);
        var digits = name?.TakeWhile(char.IsAsciiDigit).Count() ?? 0;
        return digits > 0 ? name![..digits] : null;
    }

    private static string[] Segments(string folder) =>
        folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (int Number, string Remainder)? Numbered(string label)
    {
        var digits = label.TakeWhile(char.IsAsciiDigit).Count();
        if (digits == 0 || !int.TryParse(label.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return (number, label[digits..].TrimStart(' ', '_', '-', '.'));
    }

    private static string ChapterName(int number, string rest) =>
        rest.Length == 0 || ChapterWord().IsMatch(rest)
            ? string.Create(CultureInfo.InvariantCulture, $"Capítulo {number}")
            : Humanize(rest);

    private static string Humanize(string label)
    {
        var text = string.Join(' ', label.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return text.Length == 0 ? label : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
    }

    private static bool IsExtra(string name) => string.Equals(name.Trim(), ExtraName, StringComparison.OrdinalIgnoreCase);

    private static long NumberOf(string key)
    {
        var end = key.Length;
        var start = end;
        while (start > 0 && char.IsAsciiDigit(key[start - 1]))
        {
            start--;
        }

        return start < end && long.TryParse(key.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue;
    }

    private static ScriptGroupTotals Totals(string name, int? number, List<string> keys, Dictionary<string, StatusCounts> counts)
    {
        int ready = 0, review = 0, japanese = 0;
        foreach (var key in keys)
        {
            var c = counts[key];
            ready += c.Ready;
            review += c.ToReview;
            japanese += c.Untranslated;
        }

        return new ScriptGroupTotals(name, number, keys, ready, review, japanese);
    }

    [GeneratedRegex(@"^(cap(itulo|ítulo)?|chapter|ch|c)?[\s_.\-]*\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterWord();
}
