using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Tests.Screens;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Controls;

/// <summary>
/// Estados de cada controle do design v2 com o tema e os estilos reais do app, nos dois temas: pseudo-classes, partes visíveis e as cores
/// que vêm dos tokens. Pega erro de XAML, seletor que não casa e token trocado que os testes de ViewModel não veem.
/// </summary>
public class ControlStatesHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static readonly string[] Levels = ["Baixo", "Médio", "Alto"];

    private static readonly string[] Headers = ["IA", "Formatação", "Acentuação"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private Task OnUi(string theme, Action<Ui> test) =>
        fixture.Session.Dispatch(
            () =>
            {
                using var ui = new Ui(Variant(theme));
                test(ui);
            },
            TestContext.Current.CancellationToken);

    /// <summary>A janela de teste: criada e usada na thread da sessão headless, que é onde os controles precisam nascer.</summary>
    private sealed class Ui(ThemeVariant theme) : IDisposable
    {
        private Window? window;

        public void Show(Control content)
        {
            window = new Window { Width = 900, Height = 700, RequestedThemeVariant = theme, Content = content };
            window.Show();
            Settle();
        }

        public void Settle() => ScreenHost.Settle(window!);

        public void Dispose() => window?.Close();
    }

    private static Color Token(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), $"Token {key} não existe");
        return Assert.IsType<ISolidColorBrush>(found, exactMatch: false).Color;
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsType<ISolidColorBrush>(brush, exactMatch: false).Color;

    private static T Part<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static ContentPresenter PresenterOf(Visual item) =>
        item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    // ---------------------------------------------------------------- SeverityTag

    [Fact]
    public async Task SeverityTag_anuncia_o_texto_para_leitor_de_tela()
    {
        await OnUi("Dark", ui =>
        {
            var tag = new SeverityTag { Level = SeverityLevel.Warning, Count = 1 };
            ui.Show(tag);
            Assert.Equal("1 aviso", AutomationProperties.GetName(tag));
        });
    }

    // ---------------------------------------------------------------- PipelineStepper

    private static PipelineStepper Stepper(ICommand? command = null)
    {
        var states = PipelineStepper.Resolve([true, false, false, false, false]);
        string[] titles = ["Extrair", "Traduzir", "Conferir", "Build", "Injetar"];
        return new PipelineStepper
        {
            ItemsSource = titles.Select((t, i) => new PipelineStep(i + 1, t, $"sub {t}", states[i], command, t)).ToList(),
        };
    }

    private sealed class RelayCommand(Action<object?> run) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run(parameter);
    }

    // ---------------------------------------------------------------- LineRow

    // ---------------------------------------------------------------- ScriptItem

    // ---------------------------------------------------------------- ToggleSwitch (#580)

    private static void AssertKnobCentered(ToggleSwitch toggle, Ui ui)
    {
        // A bolinha desliza por uma transição de Canvas.Left: espera ela parar antes de medir.
        var moving = Part<Grid>(toggle, "PART_MovingKnobs");
        var previous = double.NaN;
        for (var i = 0; i < 100; i++)
        {
            ui.Settle();
            var left = Avalonia.Controls.Canvas.GetLeft(moving);
            if (left == previous)
            {
                break;
            }

            previous = left;
            System.Threading.Thread.Sleep(30);
        }

        var track = Part<Border>(toggle, "OuterBorder");
        var knob = Part<Avalonia.Controls.Shapes.Ellipse>(toggle, toggle.IsChecked == true ? "SwitchKnobOn" : "SwitchKnobOff");
        var center = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), track);
        Assert.NotNull(center);
        var inset = toggle.IsChecked == true ? track.Bounds.Width - center.Value.X : center.Value.X;
        Assert.Equal(10, inset, 1);
        Assert.Equal(10, center.Value.Y, 1);
    }

    // ---------------------------------------------------------------- ToggleRule

    // ---------------------------------------------------------------- Segmentado e abas

    // ---------------------------------------------------------------- FilterChip

    // ---------------------------------------------------------------- AiSuggestion

    // ---------------------------------------------------------------- Legend

    // ---------------------------------------------------------------- StatCard e PageHeader

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task StatCard_mostra_titulo_numero_apoio_visualizacao_e_rodape(string theme)
    {
        await OnUi(theme, ui =>
        {
            var full = new StatCard { Title = "Linhas", Value = "48.211", Description = "39% concluídas", Footer = "Continuar no editor", Content = new Border { Height = 8 } };
            var bare = new StatCard { Title = "Conferência", Value = "39 problemas" };
            ui.Show(new StackPanel { Width = 320, Children = { full, bare } });
            Assert.Equal("48.211", Part<TextBlock>(full, "PART_Value").Text);
            Assert.Equal(28, Part<TextBlock>(full, "PART_Value").FontSize);
            Assert.Equal(FontWeight.Bold, Part<TextBlock>(full, "PART_Value").FontWeight);
            Assert.True(Part<Border>(full, "PART_Footer").IsVisible);
            Assert.Equal(40, Part<Border>(full, "PART_Footer").Bounds.Height);
            Assert.False(Part<Border>(bare, "PART_Footer").IsVisible);
            Assert.Equal(Token(full, "Vnf.StrokeSubtle"), ColorOf(Part<Border>(full, "PART_Footer").BorderBrush));
            Assert.Equal(320, full.Bounds.Width);
        });
    }

    [Fact]
    public async Task PageHeader_mostra_migalha_titulo_e_acoes_a_direita()
    {
        await OnUi("Dark", ui =>
        {
            var withActions = new PageHeader { Breadcrumb = "D:\\Traducoes\\minha-vn", Title = "Minha VN", Content = new Button { Content = "Abrir pasta" } };
            var bare = new PageHeader { Title = "Sem migalha" };
            ui.Show(new StackPanel { Width = 800, Children = { withActions, bare } });
            Assert.True(Part<TextBlock>(withActions, "PART_Breadcrumb").IsVisible);
            Assert.False(Part<TextBlock>(bare, "PART_Breadcrumb").IsVisible);
            Assert.Equal("Minha VN", Part<TextBlock>(withActions, "PART_Title").Text);
            Assert.Equal(24, Part<TextBlock>(withActions, "PART_Title").FontSize);

            var button = withActions.GetVisualDescendants().OfType<Button>().Single();
            var title = Part<TextBlock>(withActions, "PART_Title");
            Assert.True(button.TranslatePoint(default, withActions)!.Value.X > title.TranslatePoint(default, withActions)!.Value.X);
            Assert.Equal(800, button.TranslatePoint(new Point(button.Bounds.Width, 0), withActions)!.Value.X, 1);
        });
    }
}
