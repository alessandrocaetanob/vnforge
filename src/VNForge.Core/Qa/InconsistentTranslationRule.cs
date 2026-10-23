namespace VNForge.Core.Qa;

/// <summary>
/// Regra entre linhas: o mesmo original traduzido de jeitos diferentes no projeto inteiro. Dentro de cada grupo de originais iguais, a
/// tradução mais usada (no empate, a que aparece primeiro) é a referência e cada linha que difere dela recebe um aviso. Linhas sem
/// tradução utilizável não entram.
/// </summary>
public sealed class InconsistentTranslationRule : IQaRule
{
    public string Id => "traducao-inconsistente";

    public string Description => "Mesmo original traduzido de jeitos diferentes.";

    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckCore(context);
    }

    private IEnumerable<QaIssue> CheckCore(QaContext context)
    {
        var groups = new Dictionary<string, List<(string UnitId, string Key, string Translation)>>(StringComparer.Ordinal);
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                var source = unit.Source.Trim();
                if (source.Length == 0 || !unit.HasTranslation || unit.Translation is not { } translation)
                {
                    continue;
                }

                if (!groups.TryGetValue(source, out var list))
                {
                    groups[source] = list = [];
                }

                list.Add((unit.Id, key, translation.Trim()));
            }
        }

        foreach (var (source, list) in groups)
        {
            var reference = list
                .GroupBy(x => x.Translation, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .First().Key;
            foreach (var (unitId, key, translation) in list)
            {
                if (!string.Equals(translation, reference, StringComparison.Ordinal))
                {
                    yield return new QaIssue(Id, QaSeverity.Warning, unitId, key,
                        $"o original '{source}' aparece traduzido como '{reference}' em outras linhas");
                }
            }
        }
    }
}
