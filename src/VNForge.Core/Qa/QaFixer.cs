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
            var edited = new List<TextUnit>(units.Count);
            var touched = false;
            foreach (var unit in units)
            {
                var current = unit;
                foreach (var fixer in fixers)
                {
                    if (!current.HasTranslation || current.Flags.Contains(fixer.Id, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    var fixedText = fixer.Fix(current.Translation);
                    if (string.Equals(fixedText, current.Translation, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    fixes.Add(new QaFix(fixer.Id, current.Id, key, current.Translation, fixedText));
                    current = current with { Translation = fixedText };
                    touched = true;
                }

                edited.Add(current);
            }

            if (touched)
            {
                changed[key] = edited;
            }
        }

        return new QaFixResult(fixes, changed);
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
