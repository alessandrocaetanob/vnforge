using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using VNForge.App.Tests.Rendering;

namespace VNForge.App.Tests.Controls;

/// <summary>O botão comum é o contornado do quadro (#580, G3): só o rótulo é semibold, e o que o botão carrega dentro, o subtle e o danger seguem o quadro nos estados.</summary>
public class ButtonStyleTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private Task OnUi(Action<Window> test) =>
        fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 600, Height = 400 };
                try
                {
                    test(window);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);

    private static void Show(Window window, Control content)
    {
        // As classes meta e word-source são do LineDetail.axaml, que só o inspetor do Editor carrega.
        window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://VNForge.App/")) { Source = new Uri("avares://VNForge.App/Themes/Controls/LineDetail.axaml") });
        window.Content = new StackPanel { Children = { content } };
        window.Show();
        ScreenRenderer.Settle(window);
    }

    private static TextBlock Text(Control root, string text) =>
        Assert.Single(root.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == text);

    private static ContentPresenter Presenter(Button button) =>
        Assert.Single(button.GetVisualDescendants().OfType<ContentPresenter>(), p => p.Name == "PART_ContentPresenter");

    private static void Hover(Button button, bool on) => ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(":pointerover", on);

    private static Grid Row() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        Children =
        {
            new TextBlock { Classes = { "body" }, Text = "nome" },
            new TextBlock { Classes = { "caption", "secondary" }, Text = "detalhe" },
            new TextBlock { Classes = { "body-sm" }, Text = "contagem" },
            new TextBlock { Classes = { "meta" }, Text = "medida" },
        },
    };

    [Fact]
    public async Task Rotulo_do_botao_contornado_e_semibold()
    {
        await OnUi(window =>
        {
            var comum = new Button { Content = "Cancelar" };
            Show(window, comum);

            Assert.Equal(FontWeight.SemiBold, Text(comum, "Cancelar").FontWeight);
        });
    }

    [Theory]
    [InlineData("subtle")]
    [InlineData("subtle evalrow")]
    [InlineData("link")]
    [InlineData("word-source")]
    public async Task Texto_dentro_de_botao_de_linha_ou_link_fica_regular(string classes)
    {
        await OnUi(window =>
        {
            var button = new Button { Content = Row() };
            foreach (var name in classes.Split(' '))
            {
                button.Classes.Add(name);
            }

            Show(window, button);

            foreach (var text in new[] { "nome", "detalhe", "contagem", "medida" })
            {
                Assert.Equal(FontWeight.Normal, Text(button, text).FontWeight);
            }
        });
    }

    [Fact]
    public async Task Textos_de_corpo_dentro_do_botao_contornado_ficam_regulares()
    {
        await OnUi(window =>
        {
            var button = new Button { Content = Row() };
            Show(window, button);

            foreach (var text in new[] { "nome", "detalhe", "contagem", "medida" })
            {
                Assert.Equal(FontWeight.Normal, Text(button, text).FontWeight);
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Botao_subtle_nao_ganha_contorno_no_hover_nem_no_press(bool pressed)
    {
        await OnUi(window =>
        {
            var button = new Button { Content = "x", Classes = { "subtle" } };
            Show(window, button);
            Hover(button, true);
            if (pressed)
            {
                ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(":pressed", true);
            }

            ScreenRenderer.Settle(window);

            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(Presenter(button).BorderBrush).Color);
        });
    }

    [Theory]
    [InlineData(":pointerover")]
    [InlineData(":pressed")]
    public async Task Botao_danger_mantem_o_contorno_de_erro_no_hover_e_no_press(string state)
    {
        await OnUi(window =>
        {
            var button = new Button { Content = "Desfazer", Classes = { "danger" } };
            Show(window, button);
            ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(state, true);
            ScreenRenderer.Settle(window);

            Assert.True(window.TryFindResource("Vnf.SignalErrorFg", window.ActualThemeVariant, out var found));
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(Presenter(button).BorderBrush).Color);
        });
    }
}
