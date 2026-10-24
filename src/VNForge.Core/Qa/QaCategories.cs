namespace VNForge.Core.Qa;

/// <summary>
/// Categoria de cada regra de QA, a mesma que a tela Conferir do app mostra: o que o revisor vê agrupado, em vez de uma lista
/// plana por id de regra. Regra fora da tabela (de teste ou nova) cai em <see cref="Other"/>.
/// </summary>
public static class QaCategories
{
    public const string Markers = "Marcadores";

    public const string TextBox = "Caixa de texto";

    public const string Spaces = "Espaços";

    public const string Punctuation = "Pontuação";

    public const string Accents = "Acentuação";

    public const string Glossary = "Glossário";

    public const string Consistency = "Consistência";

    public const string Translation = "Tradução";

    public const string Other = "Outras";

    private static readonly Dictionary<string, string> ByRule = new(StringComparer.Ordinal)
    {
        ["marcador-diferente"] = Markers,
        [BoxLineLimitRule.RuleId] = TextBox,
        [FieldByteLimitRule.RuleId] = TextBox,
        ["espaco-duplo"] = Spaces,
        ["espaco-antes-pontuacao"] = Spaces,
        ["aspas-desbalanceadas"] = Punctuation,
        [UnencodableCharacterRule.RuleId] = Accents,
        ["glossario-diferente"] = Glossary,
        ["personagem-diferente"] = Glossary,
        ["traducao-inconsistente"] = Consistency,
        [UntranslatedQaRule.RuleId] = Translation,
        ["traducao-vazia"] = Translation,
        ["japones-na-traducao"] = Translation,
    };

    /// <summary>Categorias na ordem em que as telas as listam; <see cref="Other"/> vem por último.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Markers, TextBox, Spaces, Punctuation, Accents, Glossary, Consistency, Translation, Other];

    /// <summary>Categoria da regra <paramref name="ruleId"/>, ou <see cref="Other"/> quando a regra não está na tabela.</summary>
    public static string Of(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        return ByRule.GetValueOrDefault(ruleId, Other);
    }
}
