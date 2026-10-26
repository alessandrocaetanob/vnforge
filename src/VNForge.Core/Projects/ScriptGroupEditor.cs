using System.Globalization;

namespace VNForge.Core.Projects;

/// <summary>Um pedido de agrupamento que não pode ser feito: chave desconhecida, intervalo inválido, nome vazio, grupo que não existe. Nada foi gravado.</summary>
public sealed class ScriptGroupingException(string message) : Exception(message);

/// <summary>O que um agrupamento mudou: as chaves dos scripts, o grupo que passaram a ter (nulo ao desagrupar) e os capítulos como o Painel passa a mostrá-los.</summary>
public sealed record ScriptGroupChange(IReadOnlyList<string> Keys, string? Group, IReadOnlyList<ScriptGroupTotals> Chapters);

/// <summary>
/// O agrupamento manual de capítulos: a escolha da pessoa que a CLI e o app gravam em <c>scripts.json</c> (<c>group</c>) por cima do que o
/// <see cref="ScriptGrouping"/> propõe. Cada operação confere o pedido contra os scripts do projeto (<c>text/*.json</c>), grava de uma vez
/// e devolve os capítulos novos; um pedido inválido não grava nada.
/// </summary>
public static class ScriptGroupEditor
{
    private const string RangeSeparator = "..";

    /// <summary>
    /// Põe os scripts no grupo <paramref name="groupName"/>, um que já existe ou um novo; o grupo <see cref="ScriptGrouping.ExtraName"/> manda
    /// os scripts para fora da história. Cada item de <paramref name="specs"/> é uma chave (<c>yst00012</c>) ou um intervalo na ordem numérica
    /// (<c>yst00010..yst00020</c>, as duas pontas incluídas).
    /// </summary>
    /// <exception cref="ScriptGroupingException">Nome vazio, nenhuma chave, chave desconhecida ou intervalo inválido.</exception>
    /// <exception cref="Glossary.GlossaryFileException">O <c>scripts.json</c> atual é inválido.</exception>
    public static ScriptGroupChange Group(ProjectFolder project, IReadOnlyList<string> specs, string? groupName)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ScriptGroupingException("O nome do capítulo não pode ser vazio.");
        }

        return Apply(project, specs, groupName.Trim());
    }

    /// <summary>Tira a escolha da pessoa dos scripts: eles voltam ao grupo que a pasta, o prefixo ou a faixa propõe.</summary>
    /// <exception cref="ScriptGroupingException">Nenhuma chave, chave desconhecida ou intervalo inválido.</exception>
    /// <exception cref="Glossary.GlossaryFileException">O <c>scripts.json</c> atual é inválido.</exception>
    public static ScriptGroupChange Ungroup(ProjectFolder project, IReadOnlyList<string> specs)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Apply(project, specs, null);
    }

    /// <summary>
    /// Dá um novo nome ao capítulo. <paramref name="group"/> é o nome atual (sem diferenciar maiúsculas) ou o número do capítulo no Painel; um nome que já
    /// existe junta os dois capítulos.
    /// </summary>
    /// <exception cref="ScriptGroupingException">Nome novo vazio, capítulo que não existe ou o grupo <see cref="ScriptGrouping.ExtraName"/>.</exception>
    /// <exception cref="Glossary.GlossaryFileException">O <c>scripts.json</c> atual é inválido.</exception>
    public static ScriptGroupChange RenameGroup(ProjectFolder project, string group, string? newName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ScriptGroupingException("O novo nome do capítulo não pode ser vazio.");
        }

        var names = project.LoadScriptNames();
        var files = ProjectStatusReporter.Create(project).Files;
        var chapters = ScriptGrouping.Build(names, files);
        var wanted = group.Trim();
        var found = chapters.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? (int.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? chapters.FirstOrDefault(c => c.Number == number) : null)
            ?? throw new ScriptGroupingException($"Não há capítulo chamado '{wanted}'. Use `vnf scripts list` para ver os capítulos.");

        if (found.IsExtra)
        {
            throw new ScriptGroupingException($"'{ScriptGrouping.ExtraName}' não se renomeia: ele reúne o que fica fora da história. Para mudar as cenas de lugar use `vnf scripts group`.");
        }

        var renamed = ScriptGrouping.RenameGroup(names, files, found.Name, newName);
        project.SaveScriptNames(renamed);
        return new ScriptGroupChange(found.ScriptKeys, newName.Trim(), ScriptGrouping.Build(renamed, files));
    }

    private static ScriptGroupChange Apply(ProjectFolder project, IReadOnlyList<string> specs, string? groupName)
    {
        var names = project.LoadScriptNames();
        var files = ProjectStatusReporter.Create(project).Files;
        var keys = Resolve(specs, files.Select(f => f.Key));
        var changed = ScriptGrouping.Regroup(names, keys, groupName);
        project.SaveScriptNames(changed);
        return new ScriptGroupChange(keys, groupName, ScriptGrouping.Build(changed, files));
    }

    private static List<string> Resolve(IReadOnlyList<string> specs, IEnumerable<string> available)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var ordered = ScriptGrouping.InStoryOrder(available);
        var known = new HashSet<string>(ordered, StringComparer.Ordinal);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var spec in specs.SelectMany(s => s.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var range = spec.Split(RangeSeparator, 2);
            if (range.Length == 2)
            {
                chosen.UnionWith(Between(ordered, known, range[0].ToLowerInvariant(), range[1].ToLowerInvariant(), spec));
            }
            else if (known.Contains(spec.ToLowerInvariant()))
            {
                chosen.Add(spec.ToLowerInvariant());
            }
            else
            {
                unknown.Add(spec);
            }
        }

        if (unknown.Count > 0)
        {
            throw new ScriptGroupingException($"Não há script com a chave {string.Join(", ", unknown.Select(k => $"'{k}'"))}. Use `vnf scripts list` para ver as chaves.");
        }

        if (chosen.Count == 0)
        {
            throw new ScriptGroupingException("Diga ao menos uma chave de script (como yst00012) ou um intervalo (yst00010..yst00020).");
        }

        return [.. ordered.Where(chosen.Contains)];
    }

    private static IEnumerable<string> Between(IReadOnlyList<string> ordered, HashSet<string> known, string from, string to, string spec)
    {
        if (!known.Contains(from) || !known.Contains(to))
        {
            throw new ScriptGroupingException($"O intervalo '{spec}' usa uma chave que não existe no projeto. Use `vnf scripts list` para ver as chaves.");
        }

        var list = ordered.ToList();
        var start = list.IndexOf(from);
        var end = list.IndexOf(to);
        if (start > end)
        {
            throw new ScriptGroupingException($"O intervalo '{spec}' está invertido: escreva a chave menor primeiro.");
        }

        return ordered.Skip(start).Take(end - start + 1);
    }
}
