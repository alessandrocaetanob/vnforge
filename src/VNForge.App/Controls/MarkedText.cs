using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using VNForge.Core.Formatting;

namespace VNForge.App.Controls;

/// <summary>
/// Texto de leitura com os marcadores do jogo em selos: <c>MarkedText.Source</c> num <see cref="TextBlock"/> troca cada <c>\p</c> ou <c>\n</c> por um
/// <see cref="MarkerChip"/> ("virar a página", "quebra de linha"). Texto com marcador fora da gramática aparece cru, sem selos.
/// </summary>
public static class MarkedText
{
    public static readonly AttachedProperty<string?> SourceProperty = AvaloniaProperty.RegisterAttached<TextBlock, string?>("Source", typeof(MarkedText));

    static MarkedText() => SourceProperty.Changed.AddClassHandler<TextBlock>((block, e) => Fill(block, e.NewValue as string));

    public static string? GetSource(TextBlock block) => block.GetValue(SourceProperty);

    public static void SetSource(TextBlock block, string? value) => block.SetValue(SourceProperty, value);

    private static void Fill(TextBlock block, string? text)
    {
        block.Text = null;
        block.Inlines?.Clear();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        IReadOnlyList<TextSegment> segments;
        try
        {
            segments = ControlMarkers.Split(text);
        }
        catch (ArgumentException)
        {
            segments = [new TextSegment(text, IsMarker: false)];
        }

        foreach (var segment in segments)
        {
            if (!segment.IsMarker)
            {
                block.Inlines?.Add(new Run(segment.Text));
                continue;
            }

            var kind = segment.Text.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control;
            block.Inlines?.Add(new InlineUIContainer(MarkerChipGenerator.CreateChip(segment.Text, kind)) { BaselineAlignment = BaselineAlignment.Center });
        }
    }
}
