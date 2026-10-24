using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Tests;

/// <summary>Garante que as famílias do design system resolvem para a fonte embutida, e não para o fallback do sistema.</summary>
public class FontsHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static string Resolve(string resourceKey, FontWeight weight)
    {
        var family = (FontFamily)Application.Current!.FindResource(resourceKey)!;
        return FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var glyphs)
            ? glyphs.FamilyName
            : "(não resolveu)";
    }

    [Theory]
    [InlineData("Vnf.FontDisplay", 700, "Zen Kaku Gothic New")]
    [InlineData("Vnf.FontUi", 400, "Noto Sans")]
    [InlineData("Vnf.FontJp", 400, "Noto Sans JP")]
    [InlineData("Vnf.FontMono", 400, "JetBrains Mono")]
    public async Task Familia_resolve_para_a_fonte_embutida(string key, int weight, string expected)
    {
        await fixture.Session.Dispatch(
            () => Assert.Equal(expected, Resolve(key, (FontWeight)weight)),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Titulo_usa_a_fonte_de_exibicao()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var title = new TextBlock { Text = "Painel", Classes = { "title-lg" } };
                var window = new Window { Content = title };
                try
                {
                    window.Show();
                    Assert.Equal("Zen Kaku Gothic New", title.FontFamily.Name.Split(',')[0].Split('#').Last().Trim());
                    Assert.Equal(22, title.FontSize);
                    Assert.Equal(28, title.LineHeight);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }
}
