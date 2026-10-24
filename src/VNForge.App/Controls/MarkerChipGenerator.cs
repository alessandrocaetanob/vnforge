using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using AvaloniaEdit.Rendering;

namespace VNForge.App.Controls;

/// <summary>
/// Troca cada marcador do texto por um chip (<c>Border.marker</c>). O chip é um <see cref="InlineObjectElement"/>: ocupa uma coluna visual,
/// então o cursor o salta e Backspace o apaga inteiro. A borda é tracejada para marcador de controle e contínua para variável (estilo em <c>MarkerChip.axaml</c>).
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

    internal static Control CreateChip(string marker, MarkerKind kind)
    {
        var outline = new Rectangle();
        outline.Classes.Add("outline");
        var chip = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid { Children = { outline, new TextBlock { Text = marker, VerticalAlignment = VerticalAlignment.Center } } },
        };
        chip.Classes.Add("marker");
        chip.Classes.Add(kind == MarkerKind.Variable ? "variable" : "control");
        AutomationProperties.SetName(chip, MarkerLabels.Describe(marker, kind));
        return chip;
    }
}
