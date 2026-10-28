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

        foreach (var segment in Split(text))
        {
            block.Inlines?.Add(segment.IsMarker ? Chip(segment.Text) : new Run(segment.Text));
        }
    }

    /// <summary>O texto em trechos comuns e marcadores; com marcador fora da gramática, um trecho comum só.</summary>
    internal static IReadOnlyList<TextSegment> Split(string text)
    {
        try
        {
            return ControlMarkers.Split(text);
        }
        catch (ArgumentException)
        {
            return [new TextSegment(text, IsMarker: false)];
        }
    }

    /// <summary>O selo de <paramref name="marker"/> como trecho de texto, alinhado ao meio da linha.</summary>
    internal static InlineUIContainer Chip(string marker) => Embed(Create(marker, isMissing: false));

    internal static InlineUIContainer Embed(Control content) => new(content) { BaselineAlignment = BaselineAlignment.Center };

    internal static MarkerChip Create(string marker, bool isMissing) =>
        new() { Marker = marker, Kind = marker.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control, IsMissing = isMissing };
}
