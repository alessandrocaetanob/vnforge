using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// O realce da palavra do original sob o mouse (#538): fundo <c>accent-soft</c> e traço de 2 px em <c>accent</c> embaixo, como o quadro do dicionário. Desenha atrás do
/// <see cref="TextBlock"/> do original, no mesmo painel, a partir da geometria do texto já formatado; por isso não mexe na medida nem nos chips dos marcadores.
/// </summary>
public sealed class WordHighlight : Control
{
    public static readonly StyledProperty<TextBlock?> TargetProperty = AvaloniaProperty.Register<WordHighlight, TextBlock?>(nameof(Target));

    public static readonly StyledProperty<int> StartProperty = AvaloniaProperty.Register<WordHighlight, int>(nameof(Start), -1);

    public static readonly StyledProperty<int> LengthProperty = AvaloniaProperty.Register<WordHighlight, int>(nameof(Length));

    private const double LineThickness = 2;

    static WordHighlight() => AffectsRender<WordHighlight>(TargetProperty, StartProperty, LengthProperty);

    public WordHighlight()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>O texto do original, que diz onde cada caractere está.</summary>
    public TextBlock? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>A posição do primeiro caractere da palavra no texto; -1 sem palavra.</summary>
    public int Start
    {
        get => GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public int Length
    {
        get => GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    /// <summary>Os retângulos da palavra, um por linha de texto, no espaço deste controle. Vazio sem palavra ou antes de o texto ser formatado.</summary>
    public IReadOnlyList<Rect> Rects()
    {
        if (Target is not { TextLayout: { } layout } target || Start < 0 || Length <= 0)
        {
            return [];
        }

        var origin = target.TranslatePoint(default, this) ?? default;
        return [.. layout.HitTestTextRange(Start, Length).Where(r => r.Width > 0).Select(r => r.Translate(new Vector(origin.X, origin.Y)))];
    }

    public override void Render(DrawingContext context)
    {
        var fill = Brush("Vnf.AccentSoft");
        var line = Brush("Vnf.Accent");
        foreach (var rect in Rects())
        {
            context.DrawRectangle(fill, null, rect, 2, 2);
            context.DrawRectangle(line, null, new Rect(rect.Left, rect.Bottom - LineThickness, rect.Width, LineThickness));
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Transparent;
}
