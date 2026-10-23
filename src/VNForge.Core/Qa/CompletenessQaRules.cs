using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>As regras de completude (E7.2), na ordem em que entram em <see cref="QaEngine.Default"/>.</summary>
public static class CompletenessQaRules
{
    /// <summary>Linha não traduzida, tradução vazia e marcador de controle diferente do original.</summary>
    public static IReadOnlyList<IQaRule> All { get; } = [new UntranslatedQaRule(), new EmptyTranslationQaRule(), new MarkerMismatchQaRule()];
}

/// <summary>
/// Linha com original e sem tradução (<see cref="TranslationStatus.Untranslated"/> e <see cref="TextUnit.Translation"/> vazia). É aviso, não erro:
/// o build copia o original, então um build parcial é legítimo e <c>--allow-warnings</c> o libera. Rascunho em <c>untranslated</c> com
/// texto não entra aqui: é o caso da regra de tradução ignorada.
/// </summary>
public sealed class UntranslatedQaRule : UnitQaRule
{
    /// <inheritdoc />
    public override string Id => "nao-traduzida";

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
/// tradução, e o build gravaria uma caixa de texto vazia no jogo.
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
        if (unit.Status != TranslationStatus.Untranslated
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
        string? invalid = null;
        try
        {
            actual = ControlMarkers.Markers(unit.Translation);
        }
        catch (ArgumentException ex)
        {
            invalid = ex.Message.Split(" (Parameter", StringSplitOptions.None)[0];
            actual = [];
        }

        if (invalid is not null)
        {
            yield return $"marcador inválido na tradução: {invalid}";
            yield break;
        }

        var missing = Subtract(expected, actual);
        var extra = Subtract(actual, expected);
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

    private static bool IsCompared(string marker) => !string.Equals(marker, ControlMarkers.LineBreak, StringComparison.Ordinal);
}
