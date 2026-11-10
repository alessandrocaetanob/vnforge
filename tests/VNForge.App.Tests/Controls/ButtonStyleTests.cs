using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using VNForge.App.Tests.Rendering;

namespace VNForge.App.Tests.Controls;

/// <summary>O botão comum é o contornado do quadro (#580, G3): o rótulo é semibold, inclusive no subtle; a linha inteira e o link ficam regulares, e o subtle e o danger seguem o quadro nos estados.</summary>
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

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var found));
        return Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
    }

    private static Color ForegroundOf(Control root, string text) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Text(root, text).Foreground).Color;

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

    [Theory]
    [InlineData("", 16, 32)]
    [InlineData("sm", 12, 28)]
    [InlineData("lg", 16, 40)]
    public async Task Botao_tem_o_padding_e_a_altura_do_quadro(string classes, double padding, double height)
    {
        await OnUi(window =>
        {
            var button = new Button { Content = "Cancelar" };
            foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                button.Classes.Add(name);
            }

            Show(window, button);

            Assert.Equal(padding, button.Padding.Left);
            Assert.Equal(padding, button.Padding.Right);
            Assert.Equal(height, button.Bounds.Height);
        });
    }

    [Fact]
    public async Task Botao_de_icone_pequeno_tem_28_px()
    {
        await OnUi(window =>
        {
            var button = new Button { Content = "x", Classes = { "icon", "sm", "subtle" } };
            Show(window, button);

            Assert.Equal(28, button.Bounds.Width);
        });
    }

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

    [Fact]
    public async Task Rotulo_do_botao_primario_e_semibold()
    {
        await OnUi(window =>
        {
            var primario = new Button { Content = "Salvar regra", Classes = { "accent" } };
            Show(window, primario);

            Assert.Equal(FontWeight.SemiBold, Text(primario, "Salvar regra").FontWeight);
        });
    }

    [Theory]
    [InlineData("subtle")]
    [InlineData("subtle sm")]
    [InlineData("subtle lg")]
    public async Task Rotulo_do_botao_subtle_e_semibold(string classes)
    {
        await OnUi(window =>
        {
            var button = new Button { Content = "Roteiro" };
            foreach (var name in classes.Split(' '))
            {
                button.Classes.Add(name);
            }

            Show(window, button);

            Assert.Equal(FontWeight.SemiBold, Text(button, "Roteiro").FontWeight);
        });
    }

    [Fact]
    public async Task Rotulo_do_botao_subtle_e_text_secondary_em_repouso_e_text_primary_no_hover()
    {
        await OnUi(window =>
        {
            var button = new Button { Content = "Pular", Classes = { "subtle" } };
            Show(window, button);

            Assert.Equal(Brush(window, "Vnf.TextSecondary"), ForegroundOf(button, "Pular"));

            Hover(button, true);
            ScreenRenderer.Settle(window);

            Assert.Equal(Brush(window, "Vnf.TextPrimary"), ForegroundOf(button, "Pular"));
            Assert.Equal(Brush(window, "Vnf.BgCardHover"), Assert.IsAssignableFrom<ISolidColorBrush>(Presenter(button).Background).Color);
        });
    }

    [Theory]
    [InlineData("subtle evalrow")]
    [InlineData("subtle chapter")]
    [InlineData("link")]
    [InlineData("word-source")]
    public async Task Texto_dentro_de_linha_inteira_ou_link_fica_regular(string classes)
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

    [Theory]
    [InlineData("subtle evalrow")]
    [InlineData("subtle chapter")]
    public async Task Linha_inteira_subtle_fica_em_text_primary_no_repouso(string classes)
    {
        await OnUi(window =>
        {
            var button = new Button { Content = new TextBlock { Text = "Capítulo 1" } };
            foreach (var name in classes.Split(' '))
            {
                button.Classes.Add(name);
            }

            Show(window, button);

            Assert.Equal(Brush(window, "Vnf.TextPrimary"), ForegroundOf(button, "Capítulo 1"));
        });
    }

    [Fact]
    public async Task Codigo_mono_dentro_de_botao_subtle_fica_regular_e_em_text_primary()
    {
        await OnUi(window =>
        {
            var button = new Button { Content = new TextBlock { Classes = { "mono" }, Text = "さん" }, Classes = { "subtle" } };
            Show(window, button);

            Assert.Equal(FontWeight.Normal, Text(button, "さん").FontWeight);
            Assert.Equal(Brush(window, "Vnf.TextPrimary"), ForegroundOf(button, "さん"));
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

    private static Border Kbd(string text) => new()
    {
        Classes = { "kbd" },
        Child = new TextBlock { Text = text },
    };

    [Fact]
    public async Task Kbd_e_retangulo_de_raio_4_com_contorno_de_1_px_e_sem_preenchimento()
    {
        await OnUi(window =>
        {
            var kbd = Kbd("Ctrl+K");
            Show(window, kbd);

            Assert.Equal(new CornerRadius(4), kbd.CornerRadius);
            Assert.Equal(new Thickness(1), kbd.BorderThickness);
            Assert.Equal(new Thickness(5, 1), kbd.Padding);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(kbd.Background).Color);
            Assert.Equal(0.85, kbd.Opacity);
        });
    }

    [Fact]
    public async Task Kbd_usa_mono_500_de_11_px_com_linha_de_14()
    {
        await OnUi(window =>
        {
            var kbd = Kbd("Esc");
            Show(window, kbd);

            var text = Text(kbd, "Esc");
            Assert.Equal(FontWeight.Medium, text.FontWeight);
            Assert.Equal(11, text.FontSize);
            Assert.Equal(14, text.LineHeight);
            Assert.True(window.TryFindResource("Vnf.FontMono", window.ActualThemeVariant, out var mono));
            Assert.Equal(mono, text.FontFamily);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("subtle")]
    [InlineData("danger")]
    public async Task Kbd_tem_o_contorno_na_cor_do_texto_do_botao_que_o_carrega(string classes)
    {
        await OnUi(window =>
        {
            var kbd = Kbd("M");
            var button = new Button { Content = new StackPanel { Children = { new TextBlock { Text = "Depois" }, kbd } } };
            foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                button.Classes.Add(name);
            }

            Show(window, button);

            Assert.Equal(ForegroundOf(button, "M"), Assert.IsAssignableFrom<ISolidColorBrush>(kbd.BorderBrush).Color);
            Assert.Equal(ForegroundOf(button, "Depois"), ForegroundOf(button, "M"));
        });
    }

    [Fact]
    public async Task Kbd_dentro_do_botao_de_destaque_segue_em_on_accent()
    {
        await OnUi(window =>
        {
            var kbd = Kbd("Ctrl+Enter");
            var button = new Button { Content = new StackPanel { Children = { new TextBlock { Text = "Salvar" }, kbd } }, Classes = { "accent" } };
            Show(window, button);

            var onAccent = Brush(window, "Vnf.OnAccent");
            Assert.Equal(onAccent, Assert.IsAssignableFrom<ISolidColorBrush>(kbd.BorderBrush).Color);
            Assert.Equal(onAccent, ForegroundOf(button, "Ctrl+Enter"));
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(kbd.Background).Color);
            Assert.Equal(0.85, kbd.Opacity);
        });
    }
}
