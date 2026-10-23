using System.Text.RegularExpressions;

namespace VNForge.Core.Formatting;

/// <summary>Uma substituição do usuário (E4.5).</summary>
/// <param name="Pattern">Texto a procurar (literal) ou expressão regular, conforme <paramref name="Regex"/>. Não pode ser vazio.</param>
/// <param name="With">Texto que entra no lugar. Com <paramref name="Regex"/>, aceita <c>$1</c>, <c>${nome}</c> e as demais referências do .NET.</param>
/// <param name="Regex">Trata <paramref name="Pattern"/> como expressão regular do .NET.</param>
public sealed record Replacement(string Pattern, string With, bool Regex = false);

/// <summary>As substituições do usuário, em duas listas: as que rodam antes das regras do perfil e as que rodam depois. Cada lista roda na ordem em que está.</summary>
public sealed record ReplacementRules(IReadOnlyList<Replacement> Before, IReadOnlyList<Replacement> After)
{
    public static ReplacementRules None { get; } = new([], []);

    public bool IsEmpty => Before.Count == 0 && After.Count == 0;
}

/// <summary>
/// As substituições do usuário de uma fase (<see cref="FormattingPhase.UserRulesBefore"/> ou <see cref="FormattingPhase.UserRulesAfter"/>).
/// Herda <see cref="TextRunStage"/>: a substituição só vê o texto entre marcadores, então nunca altera <c>\n</c>, <c>\c</c> nem <c>{...}</c>
/// (consequência: um padrão não casa através de um marcador).
/// </summary>
public sealed class ReplacementStage : TextRunStage
{
    /// <summary>Teto de tempo de cada casamento; uma regex que passa disso (backtracking catastrófico) para o build com erro em vez de travá-lo.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly FormattingPhase _phase;
    private readonly IReadOnlyList<Compiled> _rules;

    /// <param name="listPath">Caminho JSON da lista no perfil (<c>$.replacements.before</c>), citado nas mensagens de erro.</param>
    /// <exception cref="FormattingException">Um padrão é vazio ou uma regex é inválida.</exception>
    public ReplacementStage(string id, FormattingPhase phase, IReadOnlyList<Replacement> replacements, string? listPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(replacements);

        Id = id;
        _phase = phase;
        listPath ??= phase == FormattingPhase.UserRulesBefore ? "$.replacements.before" : "$.replacements.after";
        var rules = new List<Compiled>(replacements.Count);
        for (var i = 0; i < replacements.Count; i++)
        {
            var path = $"{listPath}[{i}].pattern";
            var replacement = replacements[i];
            var error = TryCompile(replacement, out var regex);
            if (error is not null)
            {
                throw new FormattingException($"formatting.json inválido em {path}: {error}");
            }

            rules.Add(new Compiled(replacement, regex, path));
        }

        _rules = rules;
    }

    public override string Id { get; }

    public override FormattingPhase Phase => _phase;

    /// <summary>Confere o padrão da substituição. Devolve a mensagem de erro em português, ou <c>null</c> quando o padrão serve.</summary>
    public static string? Validate(Replacement replacement) => TryCompile(replacement, out _);

    protected override string ApplyToRun(string run, FormattingContext context)
    {
        foreach (var rule in _rules)
        {
            if (rule.Regex is null)
            {
                run = run.Replace(rule.Replacement.Pattern, rule.Replacement.With, StringComparison.Ordinal);
                continue;
            }

            try
            {
                run = rule.Regex.Replace(run, rule.Replacement.With);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new FormattingException(
                    $"formatting.json, {rule.Path}: a expressão regular passou de {RegexTimeout.TotalSeconds:0} s num trecho de texto; simplifique o padrão.");
            }
        }

        return run;
    }

    private static string? TryCompile(Replacement replacement, out Regex? regex)
    {
        regex = null;
        if (string.IsNullOrEmpty(replacement.Pattern))
        {
            return "o padrão não pode ser vazio.";
        }

        if (!replacement.Regex)
        {
            return null;
        }

        try
        {
            regex = new Regex(replacement.Pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return null;
        }
        catch (ArgumentException ex)
        {
            return $"expressão regular inválida ({ex.Message}).";
        }
    }

    private sealed record Compiled(Replacement Replacement, Regex? Regex, string Path);
}
