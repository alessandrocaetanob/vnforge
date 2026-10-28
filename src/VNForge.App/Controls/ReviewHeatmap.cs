using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VNForge.Core.Projects;

namespace VNForge.App.Controls;

/// <summary>
/// O mapa de calor do Diário de tradução: uma coluna por semana (12) e uma linha por dia da semana, domingo no alto, como o calendário de um mês. Cada célula
/// vai da cor do que está pendente (<c>Vnf.SegPending</c>, nenhuma linha aprovada) até a cor de pronto (<c>Vnf.SegApproved</c>, o dia mais cheio da janela), em
/// quatro passos; as duas cores vêm do tema, então o mapa acompanha o claro e o escuro. Passar o mouse numa célula diz o dia e quantas linhas foram aprovadas.
/// Sem aprovações, mostra as 84 células vazias.
/// </summary>
public sealed class ReviewHeatmap : Control
{
    public const double CellSize = 16;
    public const double Gap = 3;
    public const int Rows = 7;

    public static readonly StyledProperty<ReviewDiary?> DiaryProperty = AvaloniaProperty.Register<ReviewHeatmap, ReviewDiary?>(nameof(Diary));

    /// <summary>Quanto de cada passo do mapa vem da cor de pronto: o nível 1 é um tom claro, o 4 é a cor cheia.</summary>
    private static readonly double[] LevelMix = [0, 0.35, 0.55, 0.78, 1];

    private static readonly string[] WeekdayNames = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];

    static ReviewHeatmap()
    {
        AffectsRender<ReviewHeatmap>(DiaryProperty);
        AffectsMeasure<ReviewHeatmap>(DiaryProperty);
    }

    private readonly TextBlock tipText = new();

    public ReviewHeatmap()
    {
        ToolTip.SetTip(this, tipText);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public ReviewDiary? Diary
    {
        get => GetValue(DiaryProperty);
        set => SetValue(DiaryProperty, value);
    }

    /// <summary>A largura do mapa: 12 colunas de <see cref="CellSize"/> com <see cref="Gap"/> entre elas.</summary>
    public static double MapWidth => ReviewDiary.Weeks * CellSize + (ReviewDiary.Weeks - 1) * Gap;

    public static double MapHeight => Rows * CellSize + (Rows - 1) * Gap;

    protected override Size MeasureOverride(Size availableSize) => new(MapWidth, MapHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Sem isto os vãos entre as células não recebem o ponteiro: o mouse "sai" do mapa a cada vão e o tooltip fecha.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, MapWidth, MapHeight));
        var empty = Resolve("Vnf.SegPending", Colors.Gray);
        var full = Resolve("Vnf.SegApproved", Colors.SeaGreen);
        var brushes = LevelMix.Select(mix => (IBrush)new SolidColorBrush(Blend(empty, full, mix))).ToArray();
        var days = Diary?.Days ?? [];
        var count = days.Count == 0 ? ReviewDiary.Weeks * Rows : days.Count;
        for (var i = 0; i < count; i++)
        {
            var level = days.Count == 0 ? 0 : days[i].Level;
            context.DrawRectangle(brushes[level], null, new RoundedRect(CellRect(i / Rows, i % Rows), 3));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (TipAt(e.GetPosition(this)) is { } text)
        {
            tipText.Text = text;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HeatmapPeer(this);

    /// <summary>
    /// O texto da célula sob o ponto: "terça, 27/10: 42 linhas". O vão entre duas células vale pela célula de antes e um dia que ainda não chegou diz isso;
    /// só fora do mapa o resultado é nulo. O tooltip nunca fica sem texto, porque o ToolTipService não reabre um tooltip fechado enquanto o dono não muda.
    /// </summary>
    public string? TipAt(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > MapWidth || point.Y > MapHeight)
        {
            return null;
        }

        if (Diary is not { Days.Count: > 0 } diary)
        {
            return "nenhuma aprovação ainda";
        }

        var column = Math.Min((int)Math.Floor(point.X / (CellSize + Gap)), ReviewDiary.Weeks - 1);
        var row = Math.Min((int)Math.Floor(point.Y / (CellSize + Gap)), Rows - 1);
        var index = column * Rows + row;
        if (index >= diary.Days.Count)
        {
            return "dia que ainda não chegou";
        }

        var day = diary.Days[index];
        var lines = day.Lines switch
        {
            0 => "nenhuma linha",
            1 => "1 linha",
            var n => $"{n} linhas",
        };
        return $"{WeekdayNames[(int)day.Date.DayOfWeek]}, {day.Date.ToString("dd/MM", CultureInfo.InvariantCulture)}: {lines}";
    }

    /// <summary>O mapa é uma imagem para o leitor de tela: o nome vem de <c>AutomationProperties.Name</c>, que diz o total e os dias seguidos.</summary>
    private sealed class HeatmapPeer(ReviewHeatmap owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : base.GetNameCore();
    }

    private static Rect CellRect(int column, int row) => new(column * (CellSize + Gap), row * (CellSize + Gap), CellSize, CellSize);

    private Color Resolve(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is ISolidColorBrush brush ? brush.Color : fallback;

    private static Color Blend(Color from, Color to, double mix) => Color.FromArgb(
        255,
        (byte)Math.Round(from.R + (to.R - from.R) * mix),
        (byte)Math.Round(from.G + (to.G - from.G) * mix),
        (byte)Math.Round(from.B + (to.B - from.B) * mix));
}
