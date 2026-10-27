namespace VNForge.Core.Qa;

/// <summary>
/// Roda um conjunto de <see cref="IQaRule"/> sobre o projeto, aplica os filtros (<see cref="QaOptions"/>) e silencia os avisos que a linha
/// marcou como "ok" em <see cref="Text.TextUnit.Flags"/>. As regras concretas entram em <see cref="Default"/>.
/// </summary>
public sealed class QaEngine
{
    private readonly IReadOnlyList<IQaRule> _rules;

    /// <exception cref="ArgumentException">Id de regra vazio, fora do padrão ou repetido.</exception>
    public QaEngine(IEnumerable<IQaRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = [.. rules];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _rules.Select(rule => rule.Id))
        {
            if (!IsValidId(id))
            {
                throw new ArgumentException($"O id de regra '{id}' é inválido: use minúsculas, dígitos e hífens (marcador-diferente).", nameof(rules));
            }

            if (!seen.Add(id))
            {
                throw new ArgumentException($"Duas regras de QA usam o id '{id}'.", nameof(rules));
            }
        }
    }

    /// <summary>As regras do <c>vnf</c>. Uma linha por grupo de regras, para as issues do E7 entrarem sem conflito.</summary>
    public static QaEngine Default { get; } = new(
    [
        .. CompletenessQaRules.All,
        new GlossaryTermRule(),
        new CharacterNameRule(),
        new InconsistentTranslationRule(),
        .. TextQaRules.All,
        new UnencodableCharacterRule(),
        new FormattingConflictRule(),
        new SpeakerEncodingRule(),
        new BoxLineLimitRule(),
        new FieldByteLimitRule(),
        new HonorificsRule(),
        new GenderRule(),
    ]);

    /// <summary>Regras registradas, na ordem em que rodam.</summary>
    public IReadOnlyList<IQaRule> Rules => _rules;

    /// <summary>A regra de <paramref name="id"/>, ou nulo.</summary>
    public IQaRule? Find(string id) => _rules.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    /// <summary>Roda as regras escolhidas por <paramref name="options"/> sobre o <paramref name="context"/>.</summary>
    /// <exception cref="VNForgeException">O filtro de regras cita um id que não existe.</exception>
    public QaReport Run(QaContext context, QaOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= QaOptions.All;

        var selected = Select(options.Rules);
        var flags = context.Texts.Values.SelectMany(units => units)
            .Where(u => u.Flags.Count > 0)
            .ToDictionary(u => u.Id, u => u.Flags, StringComparer.Ordinal);

        var issues = new List<QaIssue>();
        var silenced = new List<QaIssue>();
        foreach (var rule in selected)
        {
            foreach (var issue in rule.Check(context))
            {
                if (issue.Severity < options.MinimumSeverity)
                {
                    continue;
                }

                // Só aviso se silencia: erro marcado como "ok" continua bloqueando.
                var marked = issue.Severity == QaSeverity.Warning
                    && flags.TryGetValue(issue.UnitId, out var unitFlags)
                    && unitFlags.Contains(issue.RuleId, StringComparer.Ordinal);
                (marked ? silenced : issues).Add(issue);
            }
        }

        return new QaReport(issues, silenced, [.. selected.Select(r => r.Id)]);
    }

    private IReadOnlyList<IQaRule> Select(IReadOnlyCollection<string>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return _rules;
        }

        var unknown = ids.Where(id => Find(id) is null).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            var known = _rules.Count == 0 ? "nenhuma regra está registrada" : "regras: " + string.Join(", ", _rules.Select(r => r.Id));
            throw new VNForgeException($"Regra de QA desconhecida: {string.Join(", ", unknown)} ({known}).");
        }

        return [.. _rules.Where(r => ids.Contains(r.Id, StringComparer.Ordinal))];
    }

    private static bool IsValidId(string id) =>
        id.Length > 0
        && id[0] is >= 'a' and <= 'z'
        && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !id.EndsWith('-');
}
