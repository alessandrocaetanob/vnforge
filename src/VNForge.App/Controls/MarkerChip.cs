using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Selo de um marcador do jogo (v3.css, <c>.mk</c>): uma pílula com o nome em português, "virar a página" no lugar de <c>\p</c>.
/// No editor o <see cref="MarkerChipGenerator"/> o põe no texto; nos textos só de leitura ele entra num <c>InlineUIContainer</c>.
/// <see cref="IsMissing"/> pinta o selo de erro: o marcador existe no original e falta na tradução.
/// </summary>
public sealed class MarkerChip : TemplatedControl
{
    public static readonly StyledProperty<string> MarkerProperty = AvaloniaProperty.Register<MarkerChip, string>(nameof(Marker), string.Empty);
    public static readonly StyledProperty<MarkerKind> KindProperty = AvaloniaProperty.Register<MarkerChip, MarkerKind>(nameof(Kind));
    public static readonly StyledProperty<bool> IsMissingProperty = AvaloniaProperty.Register<MarkerChip, bool>(nameof(IsMissing));

    public static readonly DirectProperty<MarkerChip, string> LabelProperty =
        AvaloniaProperty.RegisterDirect<MarkerChip, string>(nameof(Label), c => c.Label);

    public static readonly DirectProperty<MarkerChip, string?> GlyphProperty =
        AvaloniaProperty.RegisterDirect<MarkerChip, string?>(nameof(Glyph), c => c.Glyph);

    private string label = string.Empty;
    private string? glyph;

    static MarkerChip()
    {
        MarkerProperty.Changed.AddClassHandler<MarkerChip>((chip, _) => chip.Refresh());
        KindProperty.Changed.AddClassHandler<MarkerChip>((chip, _) => chip.Refresh());
        IsMissingProperty.Changed.AddClassHandler<MarkerChip>((chip, e) => chip.PseudoClasses.Set(":missing", e.NewValue is true));
    }

    /// <summary>O marcador como está no texto: <c>\p</c>, <c>\n</c>, <c>{VAR:nome}</c>.</summary>
    public string Marker
    {
        get => GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    public MarkerKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool IsMissing
    {
        get => GetValue(IsMissingProperty);
        set => SetValue(IsMissingProperty, value);
    }

    /// <summary>O nome em português que o selo mostra.</summary>
    public string Label
    {
        get => label;
        private set => SetAndRaise(LabelProperty, ref label, value);
    }

    /// <summary>O desenho que abre o selo; nulo desenha o triângulo de virar a página.</summary>
    public string? Glyph
    {
        get => glyph;
        private set => SetAndRaise(GlyphProperty, ref glyph, value);
    }

    private void Refresh()
    {
        Label = MarkerLabels.Name(Marker, Kind);
        Glyph = MarkerLabels.Glyph(Marker, Kind);
        PseudoClasses.Set(":variable", Kind == MarkerKind.Variable);
        AutomationProperties.SetName(this, MarkerLabels.Describe(Marker, Kind));
    }
}