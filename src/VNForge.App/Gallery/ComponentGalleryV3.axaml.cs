using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;

namespace VNForge.App.Gallery;

/// <summary>Os controles novos da interface v3 em todos os estados. Só existe no build Debug.</summary>
public sealed partial class ComponentGalleryV3 : UserControl
{
    private static int instances;

    public ComponentGalleryV3()
    {
        InitializeComponent();

        // O grupo do RadioButton vale para a janela inteira; sem um nome por instância, a coluna clara desmarca a escura.
        var group = $"gallery-quality-{Interlocked.Increment(ref instances)}";
        QualityFast.GroupName = group;
        QualityBalanced.GroupName = group;
        QualityBest.GroupName = group;

        BoxFits.Layout = Box([2]);
        BoxOver.Layout = Box([4]);
        BoxPages.Layout = Box([3, 2]);
        BoxLarge.Layout = Box([2]);
        BoxSmall.Layout = Box([2]);

        ContextList.ItemsSource = new[]
        {
            new ContextLine("Kenta", "「おはよう、美咲」", "Bom dia, Misaki."),
            new ContextLine("Misaki", "「今日も遅刻ギリギリだね」", "Chegando em cima da hora de novo, hein."),
        };
        ExplainList.ItemsSource = new[]
        {
            new ExplainPart("今日も", "de novo hoje"),
            new ExplainPart("遅刻ギリギリ", "chegar no limite do atraso"),
            new ExplainPart("だね", "marca de conversa leve, pede concordância"),
        };

        foreach (var key in IconKeys)
        {
            var icon = new PathIcon { Width = 20, Height = 20 };
            icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(key));
            IconGrid.Children.Add(new StackPanel
            {
                Width = 84,
                Spacing = 4,
                Children =
                {
                    icon,
                    new TextBlock { Classes = { "caption", "secondary" }, Text = key["Icon.".Length..], TextAlignment = Avalonia.Media.TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                },
            });
        }
    }

    private void OnOpenDialog(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SampleDialog.IsOpen = true;

    private void OnCloseDialog(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SampleDialog.IsOpen = false;

    /// <summary>Todo ícone de <c>Assets/Icons.axaml</c>, na ordem do arquivo.</summary>
    public static IReadOnlyList<string> IconKeys { get; } =
    [
        "Icon.Home", "Icon.Board", "Icon.Edit", "Icon.Book", "Icon.Glossary", "Icon.Shield", "Icon.Box", "Icon.Settings", "Icon.Search", "Icon.Folder",
        "Icon.Add", "Icon.Info", "Icon.Warning", "Icon.Check", "Icon.Error", "Icon.Dismiss", "Icon.Checkmark", "Icon.Sparkle", "Icon.ArrowSync", "Icon.History",
        "Icon.More", "Icon.Person", "Icon.Key", "Icon.ChevronDown", "Icon.ChevronRight", "Icon.Open", "Icon.Copy", "Icon.Bookmark", "Icon.Clock",
        "Icon.Fullscreen", "Icon.ArrowRight", "Icon.Lightbulb", "Icon.Money", "Icon.Timer", "Icon.Split", "Icon.Play", "Icon.Undo",
    ];

    /// <summary>Uma caixa de três linhas com as páginas pedidas; a linha além da terceira passa do limite.</summary>
    private static TextboxLayout Box(int[] linesPerPage)
    {
        string[] frases = ["Você não vai acreditar no", "que aconteceu hoje à tarde!", "Foi incrível, de verdade.", "Eu quase não consegui falar."];
        var pages = linesPerPage
            .Select(total => new TextboxBox([.. Enumerable.Range(0, total).Select(n =>
                new TextboxLine(frases[n % frases.Length], frases[n % frases.Length].Length, n >= 3, [new LineSegment(frases[n % frases.Length], 0, frases[n % frases.Length].Length, false)]))]))
            .ToArray();
        return new TextboxLayout("Misaki", 28, 3, pages);
    }
}