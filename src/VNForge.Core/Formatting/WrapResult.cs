namespace VNForge.Core.Formatting;

/// <summary>Resultado de <see cref="WordWrapper.Wrap"/>.</summary>
/// <param name="Text">Texto com os <c>\n</c> inseridos.</param>
/// <param name="LinesPerBox">Quantas linhas saíram em cada caixa; <c>\p</c> começa uma caixa nova.</param>
/// <param name="MaxLines">O limite recebido, para <see cref="ExceedsLineLimit"/>.</param>
public sealed record WrapResult(string Text, IReadOnlyList<int> LinesPerBox, int? MaxLines)
{
    /// <summary>Índices (base 0) das caixas com mais linhas que o limite.</summary>
    public IReadOnlyList<int> OverflowingBoxes =>
        MaxLines is { } max
            ? [.. LinesPerBox.Select((lines, i) => (lines, i)).Where(t => t.lines > max).Select(t => t.i)]
            : [];

    /// <summary>Verdadeiro se alguma caixa passou do limite de linhas; o QA do E7 usa isto.</summary>
    public bool ExceedsLineLimit => OverflowingBoxes.Count > 0;
}
