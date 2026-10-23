namespace VNForge.Core.Formatting;

/// <summary>Medidas da caixa de texto para <see cref="WordWrapper.Wrap"/>.</summary>
/// <param name="MaxWidth">Largura máxima de uma linha, em colunas. Deve ser positiva.</param>
/// <param name="MaxLines">Máximo de linhas por caixa, ou <c>null</c> para não limitar.</param>
/// <param name="CharWidth">Largura de cada caractere; <c>null</c> usa <see cref="CharWidths.HalfWidthLatin"/>.</param>
public sealed record WrapOptions(int MaxWidth, int? MaxLines = null, CharWidth? CharWidth = null)
{
    internal CharWidth Width => CharWidth ?? CharWidths.HalfWidthLatin;
}
