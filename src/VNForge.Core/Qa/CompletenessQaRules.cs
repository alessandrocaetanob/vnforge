using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>As regras de completude (E7.2), na ordem em que entram em <see cref="QaEngine.Default"/>.</summary>
public static class CompletenessQaRules
{
    /// <summary>Linha não traduzida, tradução vazia, marcador de controle diferente do original e página a mais.</summary>
    public static IReadOnlyList<IQaRule> All { get; } = [new UntranslatedQaRule(), new EmptyTranslationQaRule(), new MarkerMismatchQaRule(), new ExtraPageQaRule()];
}

/// <summary>
/// Linha com original e sem tradução (<see cref="TranslationStatus.Untranslated"/> e <see cref="TextUnit.Translation"/> vazia). É aviso, não erro:
/// o build copia o original, então um build parcial é legítimo: esta regra nunca bloqueia o build (<see cref="QaReport.Blocks"/>) e o resumo
/// do build só mostra a contagem. Os outros avisos seguem exigindo <c>--allow-warnings</c>. Rascunho em <c>untranslated</c> com
/// texto não entra aqui: é o caso da regra de tradução ignorada.
/// </summary>
public sealed class UntranslatedQaRule : UnitQaRule
{
    /// <summary>Id da regra, que o <see cref="QaReport"/> usa para não bloquear o build por ela.</summary>
    public const string RuleId = "nao-traduzida";

    /// <inheritdoc />
    public override string Id => RuleId;

    /// <inheritdoc />
    public override string Description => "Linha ainda sem tradução.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (unit.Status == TranslationStatus.Untranslated
            && !string.IsNullOrWhiteSpace(unit.Source)
            && string.IsNullOrWhiteSpace(unit.Translation))
        {
            yield return "linha não traduzida";
        }
    }
}

/// <summary>
/// Linha que já saiu de <see cref="TranslationStatus.Untranslated"/> mas tem a tradução vazia ou só espaços. Erro: o status diz que há
/// tradução, e o build gravaria uma caixa de texto vazia no jogo. A linha <see cref="TranslationStatus.Keep"/> não entra: o build copia o original.
/// </summary>
public sealed class EmptyTranslationQaRule : UnitQaRule
{
    /// <inheritdoc />
    public override string Id => "traducao-vazia";

    /// <inheritdoc />
    public override string Description => "Linha marcada como traduzida com a tradução vazia.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Error;

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (unit.Status is not (TranslationStatus.Untranslated or TranslationStatus.Keep)
            && !string.IsNullOrWhiteSpace(unit.Source)
            && string.IsNullOrWhiteSpace(unit.Translation))
        {
            yield return $"tradução vazia (status {unit.Status.ToString().ToLowerInvariant()})";
        }
    }
}

/// <summary>
/// Os marcadores de controle da tradução não são os do original. Compara como multiconjunto (ordem livre, porque a gramática da língua
/// pode reordenar variáveis), sem as quebras <c>\n</c>: o <c>WordWrapper</c> do build refaz as quebras, e a tradução tem outro tamanho.
/// Erro, porque marcador perdido ou trocado quebra o script do jogo.
/// </summary>
public sealed class MarkerMismatchQaRule : UnitQaRule
{
    /// <inheritdoc />
    public override string Id => "marcador-diferente";

    /// <inheritdoc />
    public override string Description => @"Marcadores de controle (\p, \c, {VAR:...}) diferentes do original; \n é ignorado, o build refaz as quebras.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Error;

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (string.IsNullOrWhiteSpace(unit.Translation))
        {
            yield break;
        }

        IReadOnlyList<string> expected;
        try
        {
            expected = ControlMarkers.Markers(unit.Source);
        }
        catch (ArgumentException)
        {
            // Original que o próprio formato recusa: não há o que comparar (a engine já recusaria o build).
            yield break;
        }

        IReadOnlyList<string> actual;
        var translationValid = true;
        try
        {
            actual = ControlMarkers.Markers(unit.Translation);
        }
        catch (ArgumentException)
        {
            translationValid = false;
            actual = [];
        }

        if (!translationValid)
        {
            yield return "marcador inválido na tradução (barra solta, marcador desconhecido ou chave sem fechamento)";
            yield break;
        }

        var missing = Subtract(expected, actual);
        var extra = Subtract(actual, expected).Where(marker => !IsPageBreak(marker)).ToList();
        if (missing.Count == 0 && extra.Count == 0)
        {
            yield break;
        }

        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add("falta " + string.Join(" ", missing));
        }

        if (extra.Count > 0)
        {
            parts.Add("sobra " + string.Join(" ", extra));
        }

        yield return "marcadores diferentes do original: " + string.Join("; ", parts);
    }

    /// <summary>Marcadores de <paramref name="from"/> que não têm par em <paramref name="other"/>, sem as quebras de linha.</summary>
    private static List<string> Subtract(IReadOnlyList<string> from, IReadOnlyList<string> other)
    {
        var remaining = other.Where(IsCompared).GroupBy(m => m, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var marker in from.Where(IsCompared))
        {
            if (remaining.TryGetValue(marker, out var left) && left > 0)
            {
                remaining[marker] = left - 1;
            }
            else
            {
                result.Add(marker);
            }
        }

        return result;
    }

    private static bool IsPageBreak(string marker) => string.Equals(marker, ControlMarkers.PageBreak, StringComparison.Ordinal);

    private static bool IsCompared(string marker) => !string.Equals(marker, ControlMarkers.LineBreak, StringComparison.Ordinal);
}

/// <summary>
/// A tradução tem mais viradas de página (<c>\p</c>) que o original: a fala foi dividida em páginas. Não quebra o script, então é aviso, e o
/// revisor confirma que a divisão foi de propósito. Falta de <c>\p</c> e qualquer outro marcador a mais seguem sendo erro em
/// <see cref="MarkerMismatchQaRule"/>.
/// </summary>
public sealed class ExtraPageQaRule : UnitQaRule
{
    /// <summary>Id da regra.</summary>
    public const string RuleId = "pagina-extra";

    /// <inheritdoc />
    public override string Id => RuleId;

    /// <inheritdoc />
    public override string Description => @"Tradução com mais viradas de página (\p) que o original: a fala foi dividida em páginas.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (string.IsNullOrWhiteSpace(unit.Translation))
        {
            yield break;
        }

        int extra;
        try
        {
            extra = CountPageBreaks(unit.Translation) - CountPageBreaks(unit.Source);
        }
        catch (ArgumentException)
        {
            // Marcador inválido no original ou na tradução: quem aponta é a regra de marcadores.
            yield break;
        }

        if (extra > 0)
        {
            yield return extra == 1
                ? @"1 virada de página a mais que o original; confira se a divisão foi de propósito"
                : $@"{extra} viradas de página a mais que o original; confira se a divisão foi de propósito";
        }
    }

    private static int CountPageBreaks(string text) =>
        ControlMarkers.Markers(text).Count(marker => string.Equals(marker, ControlMarkers.PageBreak, StringComparison.Ordinal));
}
