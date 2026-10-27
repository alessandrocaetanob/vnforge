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

    public static readonly DirectProperty<ChapterRow, double> BarDoneProperty =
        AvaloniaProperty.RegisterDirect<ChapterRow, double>(nameof(BarDone), o => o.BarDone);

    public static readonly DirectProperty<ChapterRow, double> BarTotalProperty =
        AvaloniaProperty.RegisterDirect<ChapterRow, double>(nameof(BarTotal), o => o.BarTotal);

    private string percentText = "0%";
    private double barDone;
    private double barTotal = 1;
    private string linesText = "0 linhas";

    static ChapterRow()
    {
        IsHereProperty.Changed.AddClassHandler<ChapterRow>((row, e) => row.PseudoClasses.Set(":here", e.NewValue is true));
        IsExtraProperty.Changed.AddClassHandler<ChapterRow>((row, e) => row.PseudoClasses.Set(":extra", e.NewValue is true));
        DoneProperty.Changed.AddClassHandler<ChapterRow>((row, _) => row.Refresh());
        ReviewProperty.Changed.AddClassHandler<ChapterRow>((row, _) => row.Refresh());
        TotalProperty.Changed.AddClassHandler<ChapterRow>((row, _) => row.Refresh());
    }

    public ChapterRow() => Refresh();

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

    /// <summary>O que a barra pinta como pronto: <see cref="ThreeStateProgress.Done"/>, ou a barra cheia num capítulo sem linhas.</summary>
    public double BarDone
    {
        get => barDone;
        private set => SetAndRaise(BarDoneProperty, ref barDone, value);
    }

    /// <summary>O total da barra: <see cref="ThreeStateProgress.Total"/>, ou 1 num capítulo sem linhas, que não tem o que faltar.</summary>
    public double BarTotal
    {
        get => barTotal;
        private set => SetAndRaise(BarTotalProperty, ref barTotal, value);
    }

    public string LinesText
    {
        get => linesText;
        private set => SetAndRaise(LinesTextProperty, ref linesText, value);
    }

    private void Refresh()
    {
        // A barra pinta de roxo o que a IA traduziu; "0%" ao lado dela esconderia o que falta. Com linhas a revisar, o texto diz quantas.
        var toReview = Math.Min(Review, Math.Max(Total - Done, 0));
        var isEmpty = Total < 0.5;

        // Um capítulo sem linhas não tem o que traduzir nem revisar: conta como pronto (100%, barra cheia), nunca 0%.
        PercentText = isEmpty ? "100%" : toReview >= 0.5 ? $"{toReview.ToString("N0", PtBr)} a revisar" : DonePercentText();
        BarDone = isEmpty ? 1 : Done;
        BarTotal = isEmpty ? 1 : Total;
        LinesText = $"{Total.ToString("N0", PtBr)} {(Math.Abs(Total - 1) < 0.5 ? "linha" : "linhas")}";
    }
}