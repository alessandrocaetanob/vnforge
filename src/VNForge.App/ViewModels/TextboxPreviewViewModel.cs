using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha da caixa: o texto como o build o gravaria, as colunas que ocupa, os trechos dela na régua de colunas e se passa do limite de linhas do perfil.</summary>
public sealed record TextboxLine(string Text, int Columns, bool Overflow, IReadOnlyList<LineSegment> Segments);

/// <summary>Uma caixa de texto do jogo (o texto entre dois <c>\p</c>).</summary>
public sealed record TextboxBox(IReadOnlyList<TextboxLine> Lines);

/// <summary>O que o controle <c>GameTextbox</c> desenha.</summary>
/// <param name="Speaker">Nome da placa, o que o build grava na caixa de nome (a tradução do Glossário, ou o falante escolhido na linha); nulo quando a linha não tem falante.</param>
/// <param name="Columns">Largura da caixa em colunas.</param>
/// <param name="MaxLines">Limite de linhas por caixa do perfil; nulo sem limite.</param>
/// <param name="Boxes">As caixas, com as linhas já quebradas.</param>
public sealed record TextboxLayout(string? Speaker, int Columns, int? MaxLines, IReadOnlyList<TextboxBox> Boxes);

/// <summary>
/// Prévia da caixa de texto do painel de detalhe: a linha selecionada depois do mesmo pipeline do build e do <c>vnf preview</c>
/// (<see cref="LinePreviewer"/>), refeita a cada <see cref="Update"/>. Não quebra texto por conta própria.
/// </summary>
public sealed partial class TextboxPreviewViewModel : ObservableObject
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly TextUnit unit;
    public TextboxPreviewViewModel(TextUnit unit, FormattingProfile profile, string translation, SpeakerBoxMarks marks = default, SpeakerNaming? naming = null)
    {
        this.unit = unit ?? throw new ArgumentNullException(nameof(unit));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Marks = marks;
        Naming = naming ?? SpeakerNaming.None;
        Update(translation);
    }

    /// <summary>De onde vem o nome da placa: o que o build grava na caixa de nome (#464), não o nome do jogo.</summary>
    public SpeakerNaming Naming { get; }

    /// <summary>O perfil com que a prévia foi montada.</summary>
    public FormattingProfile Profile { get; }

    /// <summary>A abertura e o fechamento que o jogo desenha em volta da fala com falante (#453); a primeira linha da caixa ganha a abertura, e a última, o fechamento.</summary>
    public SpeakerBoxMarks Marks { get; }

    /// <summary>Por que a caixa não mostra o que o build faria, quando o build grava a linha como está (o mesmo texto do <c>vnf preview</c>); nulo caso contrário.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; private set; }

    public bool HasNotice => Notice is not null;

    /// <summary>A caixa para desenhar; nula sem tradução ou quando o texto tem um marcador que o pipeline recusa (veja <see cref="Problem"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLayout))]
    public partial TextboxLayout? Layout { get; private set; }

    /// <summary>Linhas e colunas, ou o que falta para a prévia aparecer.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>Avisos do pipeline, como caixa acima do limite de linhas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    public partial IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>Por que a prévia não pôde ser montada; nulo quando está montada ou não há texto.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    public bool HasLayout => Layout is not null;

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Refaz a prévia para o texto que está no campo de tradução, gravado ou não.</summary>
    public void Update(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);

        if (string.IsNullOrWhiteSpace(translation))
        {
            Show(null, "Escreva a tradução para ver a caixa de texto.", [], null, null);
            return;
        }

        LinePreview preview;
        try
        {
            preview = LinePreviewer.Create(Profile, unit, translation, Marks);
        }
        catch (VNForgeException e)
        {
            Show(null, string.Empty, [], e.Message, null);
            return;
        }

        var longest = preview.Boxes.SelectMany(box => box).Select(line => line.Columns).DefaultIfEmpty().Max();
        var columns = Math.Max(1, preview.WrapEnabled ? Math.Max(preview.MaxWidth, longest) : longest);
        var boxes = preview.Boxes
            .Select(box => new TextboxBox([.. box.Select((line, n) => new TextboxLine(line.Text, line.Columns, preview.Formatted && preview.MaxLines is { } max && n >= max, line.Segments))]))
            .ToArray();
        Show(new TextboxLayout(Naming.NameOf(unit), columns, preview.Formatted ? preview.MaxLines : null, boxes), Describe(preview, columns), preview.Warnings, null, preview.Notice);
    }

    private void Show(TextboxLayout? layout, string summary, IReadOnlyList<string> warnings, string? problem, string? notice)
    {
        Layout = layout;
        Summary = summary;
        Warnings = warnings;
        Problem = problem;
        Notice = notice;
    }

    private static string Describe(LinePreview preview, int longest)
    {
        var lines = Count(preview.LineCount, "linha", "linhas");
        return preview.WrapEnabled
            ? $"{lines} · largura máxima {Count(preview.MaxWidth, "coluna", "colunas")}"
            : $"{lines} · linha mais larga com {Count(longest, "coluna", "colunas")} · quebra desligada no perfil";
    }

    private static string Count(int value, string singular, string plural) =>
        $"{value.ToString("N0", PortugueseBrazil)} {(value == 1 ? singular : plural)}";
}
