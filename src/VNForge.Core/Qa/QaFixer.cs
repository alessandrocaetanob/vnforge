using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>Uma linha que o <c>--fix</c> mudou.</summary>
/// <param name="RuleId">Regra cuja correção mudou o texto.</param>
/// <param name="UnitId">Id da linha.</param>
/// <param name="ScriptKey">Chave do script da linha.</param>
/// <param name="Before">Tradução antes desta correção.</param>
/// <param name="After">Tradução depois desta correção.</param>
public sealed record QaFix(string RuleId, string UnitId, string ScriptKey, string Before, string After);

/// <summary>Resultado de uma correção: o que mudou e as unidades já corrigidas dos scripts afetados.</summary>
/// <param name="Fixes">Cada mudança, na ordem das regras e dos scripts; vazio quando não havia o que corrigir.</param>
/// <param name="Texts">Só os scripts que mudaram, com todas as unidades deles.</param>
public sealed record QaFixResult(IReadOnlyList<QaFix> Fixes, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts);

/// <summary>
/// Correção automática (<c>vnf check --fix</c>): aplica as regras do <see cref="QaEngine"/> que implementam <see cref="IQaFixer"/> e só mexe no
/// que é seguro. Idempotente: uma segunda rodada sobre o resultado não encontra nada. Respeita o "ok" do revisor (<see cref="TextUnit.Flags"/>)
/// e deixa de fora linhas sem tradução; o <see cref="TextUnit.Status"/> não muda.
/// </summary>
public static class QaFixer
{
    /// <summary>Calcula as correções sobre <paramref name="texts"/> sem gravar nada.</summary>
    /// <param name="rules">Só estas regras corrigem; nulo ou vazio usa todas as que sabem corrigir.</param>
    /// <exception cref="VNForgeException">O filtro cita um id que o <paramref name="engine"/> não conhece.</exception>
    public static QaFixResult Plan(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, QaEngine engine, IReadOnlyCollection<string>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(engine);

        if (rules is { Count: > 0 })
        {
            var unknown = rules.Where(id => engine.Find(id) is null).Distinct(StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
            {
                throw new VNForgeException($"Regra de QA desconhecida: {string.Join(", ", unknown)}.");
            }

            var notFixable = rules.Where(id => engine.Find(id) is not IQaFixer).Distinct(StringComparer.Ordinal).ToList();
            if (notFixable.Count > 0)
            {
                throw new VNForgeException($"A regra não tem correção automática: {string.Join(", ", notFixable)}.");
            }
        }

        var fixers = engine.Rules.OfType<IQaFixer>()
            .Where(f => rules is not { Count: > 0 } || rules.Contains(f.Id, StringComparer.Ordinal))
            .ToList();

        var fixes = new List<QaFix>();
        var changed = new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        foreach (var (key, units) in texts)
        {
            var edited = units.Select(unit => FixUnit(unit, key, fixers, fixes)).ToList();
            if (fixes.Exists(f => f.ScriptKey == key))
            {
                changed[key] = edited;
            }
        }

        return new QaFixResult(fixes, changed);
    }

    /// <summary>
    /// A correção que cada ocorrência de <paramref name="issues"/> teria, sem gravar nada: uma por linha e regra que sabem corrigir, calculada
    /// sobre a tradução gravada (as regras não se encadeiam, ao contrário de <see cref="Plan"/>). Ocorrência de regra sem <see cref="IQaFixer"/>,
    /// de linha sem tradução ou que a linha silenciou (<see cref="TextUnit.Flags"/>) não entra; também não entra a que o corretor deixa como está.
    /// </summary>
    public static IReadOnlyList<QaFix> Propose(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, QaEngine engine, IEnumerable<QaIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(issues);

        var units = new Dictionary<string, TextUnit>(StringComparer.Ordinal);
        foreach (var unit in texts.Values.SelectMany(list => list))
        {
            units[unit.Id] = unit;
        }

        var seen = new HashSet<(string UnitId, string RuleId)>();
        var proposals = new List<QaFix>();
        foreach (var issue in issues)
        {
            if (engine.Find(issue.RuleId) is not IQaFixer fixer
                || !units.TryGetValue(issue.UnitId, out var unit)
                || !seen.Add((issue.UnitId, issue.RuleId)))
            {
                continue;
            }

            var fix = FixOne(unit, issue.ScriptKey, fixer);
            if (fix is not null)
            {
                proposals.Add(fix);
            }
        }

        return proposals;
    }

    private static QaFix? FixOne(TextUnit unit, string key, IQaFixer fixer)
    {
        if (!unit.HasTranslation || unit.Flags.Contains(fixer.Id, StringComparer.Ordinal))
        {
            return null;
        }

        var fixedText = fixer.Fix(unit.Translation);
        return string.Equals(fixedText, unit.Translation, StringComparison.Ordinal) ? null : new QaFix(fixer.Id, unit.Id, key, unit.Translation, fixedText);
    }

    /// <summary>
    /// Aplica a correção de <paramref name="ruleId"/> só à linha <paramref name="unitId"/> do script <paramref name="scriptKey"/> e grava o script.
    /// Devolve nulo, sem gravar, quando a linha não existe, não tem tradução, foi silenciada ou o corretor não muda nada.
    /// </summary>
    /// <exception cref="VNForgeException">A regra não existe ou não tem correção automática.</exception>
    public static QaFix? ApplyOne(ProjectFolder project, QaEngine engine, string scriptKey, string unitId, string ruleId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(engine);
        var rule = engine.Find(ruleId) ?? throw new VNForgeException($"Regra de QA desconhecida: {ruleId}.");
        if (rule is not IQaFixer fixer)
        {
            throw new VNForgeException($"A regra não tem correção automática: {ruleId}.");
        }

        var units = project.LoadText(scriptKey);
        var index = -1;
        for (var i = 0; i < units.Count; i++)
        {
            if (string.Equals(units[i].Id, unitId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        var fix = index < 0 ? null : FixOne(units[index], scriptKey, fixer);
        if (fix is null)
        {
            return null;
        }

        var edited = units.ToList();
        edited[index] = edited[index] with { Translation = fix.After };
        project.SaveText(scriptKey, edited);
        return fix;
    }

    private static TextUnit FixUnit(TextUnit unit, string key, List<IQaFixer> fixers, List<QaFix> fixes)
    {
        var current = unit;
        foreach (var fixer in fixers)
        {
            if (FixOne(current, key, fixer) is { } fix)
            {
                fixes.Add(fix);
                current = current with { Translation = fix.After };
            }
        }

        return current;
    }

    /// <summary>Corrige o projeto: lê os textos, calcula e grava só os scripts que mudaram. Com <paramref name="dryRun"/> não grava.</summary>
    /// <exception cref="VNForgeException">Filtro de regras com id desconhecido.</exception>
    public static QaFixResult Apply(ProjectFolder project, QaEngine engine, IReadOnlyCollection<string>? rules = null, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        var result = Plan(project.LoadAllText(), engine, rules);
        if (!dryRun)
        {
            foreach (var (key, units) in result.Texts)
            {
                project.SaveText(key, units);
            }
        }

        return result;
    }
}
