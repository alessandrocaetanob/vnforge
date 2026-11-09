using System.Text.RegularExpressions;
using VNForge.Core.Formatting;

namespace VNForge.Core.Dictionaries;

/// <summary>
/// As regras de um dicionário como etapa do pipeline (<see cref="FormattingPhase.PostDictionary"/>). Herda <see cref="TextRunStage"/>: só vê o texto entre
/// marcadores, então nunca altera <c>\n</c>, <c>\c</c> nem <c>{...}</c>. Cada regra só roda quando todas as condições batem com a
/// <see cref="FormattingContext.Line"/>; sem linha, só as regras sem condição rodam. Regra desligada não roda.
/// </summary>
public sealed class DictionaryStage : TextRunStage
{
    private readonly IReadOnlyList<Compiled> _rules;

    /// <param name="listPath">Caminho JSON da lista (<c>$.dictionary.post</c>), citado nas mensagens de erro.</param>
    /// <exception cref="FormattingException">Um padrão é vazio ou uma regex é inválida.</exception>
    public DictionaryStage(string id, IReadOnlyList<DictionaryRule> rules, string listPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(listPath);

        Id = id;
        var compiled = new List<Compiled>(rules.Count);
        for (var i = 0; i < rules.Count; i++)
        {
            var path = $"{listPath}[{i}].pattern";
            var rule = rules[i];
            if (ReplacementStage.TryCompile(rule.AsReplacement(), out var regex) is { } error)
            {
                throw new FormattingException($"formatting.json inválido em {path}: {error}");
            }

            if (rule.Enabled)
            {
                compiled.Add(new Compiled(rule, regex, path));
            }
        }

        _rules = compiled;
    }

    public override string Id { get; }

    public override FormattingPhase Phase => FormattingPhase.PostDictionary;

    protected override string ApplyToRun(string run, FormattingContext context)
    {
        foreach (var rule in _rules)
        {
            if (rule.Rule.Conditions.Count > 0 && (context.Line is not { } line || !line.Matches(rule.Rule.Conditions)))
            {
                continue;
            }

            run = ReplacementStage.Replace(run, rule.Rule.AsReplacement(), rule.Regex, rule.Path);
        }

        return run;
    }

    /// <summary>Confere o padrão e as condições da regra. Devolve a mensagem de erro em português, ou <c>null</c> quando a regra serve.</summary>
    public static string? Validate(DictionaryRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (ReplacementStage.TryCompile(rule.AsReplacement(), out _) is { } error)
        {
            return error;
        }

        return rule.Conditions.Any(c => string.IsNullOrWhiteSpace(c.Value)) ? "a condição precisa de um valor." : null;
    }

    private sealed record Compiled(DictionaryRule Rule, Regex? Regex, string Path);
}

/// <summary>
/// O pré-dicionário: normaliza o original antes de ele ir à IA (gaguejo, dialeto, grafia alternativa). O texto salvo no projeto não muda; só o que o
/// <see cref="Apply(TextUnit)"/> devolve, que é o que o lote leva à IA e o que a chave do cache de traduções lê. As condições olham o original sem troca.
/// </summary>
public sealed class PreDictionary
{
    private readonly DictionaryStage? _stage;
    private readonly DictionaryContext _context;

    /// <param name="rules">As regras do pré (<see cref="DictionaryRules.Pre"/>).</param>
    /// <param name="context">Os nomes de script e personagens para as condições de falante e cena.</param>
    /// <exception cref="FormattingException">Um padrão é vazio ou uma regex é inválida.</exception>
    public PreDictionary(IReadOnlyList<DictionaryRule> rules, DictionaryContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _context = context ?? DictionaryContext.Empty;
        _stage = rules.Any(r => r.Enabled) ? new DictionaryStage("dictionary-pre", rules, "$.dictionary.pre") : null;
    }

    /// <summary>Sem regra ligada: <see cref="Apply(TextUnit)"/> devolve a linha como está.</summary>
    public static PreDictionary None { get; } = new([]);

    /// <summary>Lê de <paramref name="project"/> as regras do pré e o contexto das condições.</summary>
    /// <exception cref="VNForgeException">Arquivo do projeto inválido ou regra com padrão inválido.</exception>
    public static PreDictionary For(Projects.ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var rules = project.LoadFormatting().Dictionary.Pre;
        return rules.Any(r => r.Enabled) ? new PreDictionary(rules, DictionaryContext.For(project)) : None;
    }

    public bool IsEmpty => _stage is null;

    /// <summary>A linha com o original normalizado; a mesma instância quando nenhuma regra mudou o texto.</summary>
    /// <exception cref="FormattingException">Uma regra alterou um marcador de controle ou a regex passou do tempo limite.</exception>
    public Text.TextUnit Apply(Text.TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_stage is null || unit.Source.Length == 0)
        {
            return unit;
        }

        string[] before;
        try
        {
            before = [.. ControlMarkers.Markers(unit.Source)];
        }
        catch (ArgumentException)
        {
            return unit;
        }

        var result = _stage.Apply(unit.Source, new FormattingContext { Line = _context.Line(unit) });
        if (string.Equals(result, unit.Source, StringComparison.Ordinal))
        {
            return unit;
        }

        string[] after;
        try
        {
            after = [.. ControlMarkers.Markers(result)];
        }
        catch (ArgumentException)
        {
            after = [];
        }

        if (!before.SequenceEqual(after, StringComparer.Ordinal))
        {
            throw new FormattingException($"O pré-dicionário alterou os marcadores de controle da linha {unit.Id}; uma regra não pode escrever nem tirar marcador.");
        }

        return unit with { Source = result };
    }

    /// <summary>Normaliza cada linha da lista, na mesma ordem.</summary>
    public IReadOnlyList<Text.TextUnit> Apply(IReadOnlyList<Text.TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        return _stage is null ? units : [.. units.Select(Apply)];
    }
}
