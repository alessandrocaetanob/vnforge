using System.Globalization;

namespace VNForge.Core.Formatting;

/// <summary>A tradução depois do pipeline e os avisos das etapas (por exemplo, caixa acima do limite de linhas).</summary>
public sealed record FormattedText(string Text, IReadOnlyList<string> Warnings);

/// <summary>
/// O pipeline de formatação (E4.1): aplica as etapas na ordem das <see cref="FormattingPhase"/> (a do doc 02), sobre a tradução com
/// marcadores. Depois de cada etapa confere que os marcadores de controle continuam os mesmos, na mesma ordem; só a etapa que declara
/// <see cref="IFormattingStage.MayChangeLineBreaks"/> pode acrescentar <c>\n</c>. Uma etapa que viola isso lança
/// <see cref="FormattingException"/>, então nenhuma regra, nem a de um futuro épico, estraga um marcador sem que um teste falhe.
/// </summary>
/// <remarks>
/// Para acrescentar uma regra: escreva uma <see cref="IFormattingStage"/> (de texto comum, herde <see cref="TextRunStage"/>), dê a ela a
/// fase certa e registre-a em <see cref="Create"/> a partir de uma nova propriedade do <see cref="FormattingProfile"/>.
/// </remarks>
public sealed class FormattingPipeline
{
    private readonly IReadOnlyList<IFormattingStage> _stages;

    /// <param name="stages">Etapas em qualquer ordem; rodam por fase e, dentro da fase, na ordem recebida.</param>
    public FormattingPipeline(IEnumerable<IFormattingStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _stages = [.. stages.Select((stage, index) => (stage, index)).OrderBy(t => t.stage.Phase).ThenBy(t => t.index).Select(t => t.stage)];
    }

    /// <summary>As etapas, na ordem em que rodam.</summary>
    public IReadOnlyList<IFormattingStage> Stages => _stages;

    /// <summary>Monta o pipeline das regras ligadas em <paramref name="profile"/>.</summary>
    public static FormattingPipeline Create(FormattingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var stages = new List<IFormattingStage>();
        if (profile.Wrap.Enabled)
        {
            stages.Add(new WrapStage(profile.Wrap));
        }

        return new FormattingPipeline(stages);
    }

    /// <exception cref="ArgumentException">A tradução tem um marcador inválido.</exception>
    /// <exception cref="FormattingException">Uma etapa alterou um marcador de controle.</exception>
    public FormattedText Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var context = new FormattingContext();
        var current = text;
        foreach (var stage in _stages)
        {
            var before = ControlMarkers.Markers(current);
            var next = stage.Apply(current, context) ?? throw new FormattingException($"A etapa de formatação '{stage.Id}' devolveu texto nulo.");
            CheckMarkers(stage, before, next);
            current = next;
        }

        return new FormattedText(current, context.Warnings);
    }

    private static void CheckMarkers(IFormattingStage stage, IReadOnlyList<string> before, string after)
    {
        IReadOnlyList<string> markers;
        try
        {
            markers = ControlMarkers.Markers(after);
        }
        catch (ArgumentException ex)
        {
            throw new FormattingException($"A etapa de formatação '{stage.Id}' deixou um marcador de controle inválido: {ex.Message}");
        }

        var same = stage.MayChangeLineBreaks
            ? Enumerable.SequenceEqual(WithoutLineBreaks(before), WithoutLineBreaks(markers), StringComparer.Ordinal)
                && markers.Count - WithoutLineBreaks(markers).Count() >= before.Count - WithoutLineBreaks(before).Count()
            : Enumerable.SequenceEqual(before, markers, StringComparer.Ordinal);
        if (!same)
        {
            throw new FormattingException(string.Create(
                CultureInfo.InvariantCulture,
                $"A etapa de formatação '{stage.Id}' alterou os marcadores de controle da linha (antes: {Describe(before)}; depois: {Describe(markers)})."));
        }
    }

    private static IEnumerable<string> WithoutLineBreaks(IReadOnlyList<string> markers) =>
        markers.Where(m => !string.Equals(m, ControlMarkers.LineBreak, StringComparison.Ordinal));

    private static string Describe(IReadOnlyList<string> markers) =>
        markers.Count == 0 ? "nenhum" : string.Join(' ', markers);
}

/// <summary>A quebra de linha por palavra (E4.4) como etapa do pipeline.</summary>
internal sealed class WrapStage(WrapRule rule) : IFormattingStage
{
    private readonly WrapOptions _options = rule.ToOptions();

    public string Id => "wrap";

    public FormattingPhase Phase => FormattingPhase.LineBreak;

    public bool MayChangeLineBreaks => true;

    public string Apply(string text, FormattingContext context)
    {
        var result = WordWrapper.Wrap(text, _options);
        foreach (var box in result.OverflowingBoxes)
        {
            context.Warn(string.Create(
                CultureInfo.InvariantCulture,
                $"caixa {box + 1} com {result.LinesPerBox[box]} linhas (máximo {rule.MaxLines})."));
        }

        return result.Text;
    }
}
