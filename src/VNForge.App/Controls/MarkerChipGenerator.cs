using Avalonia.Controls;
using AvaloniaEdit.Rendering;

namespace VNForge.App.Controls;

/// <summary>
/// Troca cada marcador do texto por um <see cref="MarkerChip"/>, o selo com o nome em português ("virar a página"). O selo é um <see cref="InlineObjectElement"/>:
/// ocupa uma coluna visual, então o cursor o salta e Backspace o apaga inteiro. O estilo está em <c>MarkerChip.axaml</c>.
/// </summary>
public sealed class MarkerChipGenerator : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.Document.GetLineByOffset(startOffset);
        var text = CurrentContext.Document.GetText(line.Offset, line.Length);
        return MarkerScanner.FindFrom(text, startOffset - line.Offset) is { } span ? line.Offset + span.Start : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.Document.GetLineByOffset(offset);
        var text = CurrentContext.Document.GetText(line.Offset, line.Length);
        if (MarkerScanner.FindFrom(text, offset - line.Offset) is not { } span || span.Start != offset - line.Offset)
        {
            return null;
        }

        return new InlineObjectElement(span.Length, CreateChip(text.Substring(span.Start, span.Length), span.Kind));
    }

    internal static Control CreateChip(string marker, MarkerKind kind) => new MarkerChip { Marker = marker, Kind = kind };
}