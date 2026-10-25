using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Uma linha do sumário de capítulos do Painel (v3.css, <c>.chrow</c>): número, nome, a <see cref="Seg3Bar"/>, a parte pronta, as linhas e uma nota.
/// <see cref="IsHere"/> marca o capítulo aberto (<c>:here</c>) e <see cref="IsExtra"/> a última linha, "Menus e nomes", que não tem número porque não faz parte da história.
/// </summary>
public sealed class ChapterRow : ThreeStateProgress
{
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static readonly StyledProperty<int?> NumberProperty = AvaloniaProperty.Register<ChapterRow, int?>(nameof(Number));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ChapterRow, string?>(nameof(Title));
    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<ChapterRow, string?>(nameof(Note));
    public static readonly StyledProperty<bool> IsHereProperty = AvaloniaProperty.Register<ChapterRow, bool>(nameof(IsHere));
    public static readonly StyledProperty<bool> IsExtraProperty = AvaloniaProperty.Register<ChapterRow, bool>(nameof(IsExtra));

    public static readonly DirectProperty<ChapterRow, string> PercentTextProperty =
        AvaloniaProperty.RegisterDirect<ChapterRow, string>(nameof(PercentText), o => o.PercentText);

    public static readonly DirectProperty<ChapterRow, string> LinesTextProperty =
        AvaloniaProperty.RegisterDirect<ChapterRow, string>(nameof(LinesText), o => o.LinesText);

    private string percentText = "0%";
    private string linesText = "0 linhas";

    static ChapterRow()
    {
        IsHereProperty.Changed.AddClassHandler<ChapterRow>((row, e) => row.PseudoClasses.Set(":here", e.NewValue is true));
        IsExtraProperty.Changed.AddClassHandler<ChapterRow>((row, e) => row.PseudoClasses.Set(":extra", e.NewValue is true));
        DoneProperty.Changed.AddClassHandler<ChapterRow>((row, _) => row.Refresh());
        TotalProperty.Changed.AddClassHandler<ChapterRow>((row, _) => row.Refresh());
    }

    /// <summary>O número do capítulo na ordem da história; nulo em "Menus e nomes".</summary>
    public int? Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>A nota da linha: "Você está aqui · 1 erro em Manhã de chuva", "A IA parou no limite de gasto".</summary>
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public bool IsHere
    {
        get => GetValue(IsHereProperty);
        set => SetValue(IsHereProperty, value);
    }

    public bool IsExtra
    {
        get => GetValue(IsExtraProperty);
        set => SetValue(IsExtraProperty, value);
    }

    public string PercentText
    {
        get => percentText;
        private set => SetAndRaise(PercentTextProperty, ref percentText, value);
    }

    public string LinesText
    {
        get => linesText;
        private set => SetAndRaise(LinesTextProperty, ref linesText, value);
    }

    private void Refresh()
    {
        PercentText = $"{Percent}%";
        LinesText = $"{Total.ToString("N0", PtBr)} {(Math.Abs(Total - 1) < 0.5 ? "linha" : "linhas")}";
    }
}