using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Tests.Rendering;

namespace VNForge.App.Tests;

/// <summary>A caixa de texto do tema (#580) nos dois temas: fundo bg-inset, texto text-primary, contorno stroke-control e dica text-tertiary, valores do quadro v6.</summary>
public sealed class TextBoxThemeHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static (Color Fundo, Color Texto, Color Borda, Color Dica) Medir(TextBox box)
    {
        var borda = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
        var dica = box.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Placeholder");
        return (Cor(borda.Background), Cor(box.Foreground), Cor(borda.BorderBrush), Cor(dica.Foreground));
    }

    private static Color Cor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Hex(string hex) => Color.Parse(hex);

    public static TheoryData<ThemeVariant, string, string, string, string, string> Quadro => new()
    {
        { ThemeVariant.Dark, "#121615", "#ecf0ee", "#636f6b", "#84908b", "#2b3230" },
        { ThemeVariant.Light, "#eceeeb", "#161b19", "#7f8b82", "#656e69", "#dde1dd" },
    };

    [Theory]
    [MemberData(nameof(Quadro))]
    public Task Campo_normal_pointerover_e_foco_levam_as_cores_do_quadro(ThemeVariant tema, string fundo, string texto, string borda, string dica, string bordaDesativada) =>
        fixture.Session.Dispatch<int>(
            () =>
            {
                var box = new TextBox { Text = "", PlaceholderText = "Buscar", Width = 240 };
                var outro = new TextBox { Text = "x", Width = 240 };
                var window = new Window { Width = 400, Height = 200, RequestedThemeVariant = tema, Content = new StackPanel { Children = { box, outro } } };
                window.Show();
                ScreenRenderer.Settle(window);
                try
                {
                    var normal = Medir(box);
                    Assert.Equal((Hex(fundo), Hex(texto), Hex(borda), Hex(dica)), normal);

                    ((IPseudoClasses)box.Classes).Set(":pointerover", true);
                    ScreenRenderer.Settle(window);
                    Assert.Equal((Hex(fundo), Hex(texto), Hex(borda), Hex(dica)), Medir(box));
                    ((IPseudoClasses)box.Classes).Set(":pointerover", false);

                    box.Focus();
                    ScreenRenderer.Settle(window);
                    Assert.True(box.IsFocused);
                    var foco = Medir(box);
                    Assert.Equal((Hex(fundo), Hex(texto), Hex(dica)), (foco.Fundo, foco.Texto, foco.Dica));
                    Assert.True(box.TryFindResource("Vnf.Accent", tema, out var acento));
                    Assert.Equal(Cor((IBrush)acento!), foco.Borda);
                    box.IsEnabled = true;
                    outro.Focus();

                    box.IsEnabled = false;
                    ScreenRenderer.Settle(window);
                    Assert.Equal((Hex(fundo), Hex(dica), Hex(bordaDesativada), Hex(dica)), Medir(box));
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
}
