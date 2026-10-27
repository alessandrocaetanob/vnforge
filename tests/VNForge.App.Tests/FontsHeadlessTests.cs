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

    [Theory]
    [InlineData("title-lg", 26, 38)]
    [InlineData("title-md", 17, 25)]
    public async Task Titulo_usa_a_fonte_de_exibicao(string cssClass, double size, double lineHeight)
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var title = new TextBlock { Text = "Painel", Classes = { cssClass } };
                var window = new Window { Content = title };
                try
                {
                    window.Show();
                    Assert.Equal("Zen Kaku Gothic New", title.FontFamily.Name.Split(',')[0].Split('#').Last().Trim());
                    Assert.Equal(size, title.FontSize);
                    Assert.Equal(lineHeight, title.LineHeight);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>A altura natural de uma linha do <paramref name="text"/> (ascendente, descendente e entrelinha da fonte): abaixo disso o TextBlock corta as descendentes.</summary>
    internal static double NaturalLineHeight(TextBlock text)
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), out var glyphs));
        var metrics = glyphs.Metrics;
        return (metrics.Descent - metrics.Ascent + metrics.LineGap) * text.FontSize / metrics.DesignEmHeight;
    }

    [Theory]
    [InlineData("title-lg")]
    [InlineData("title-md")]
    public async Task Titulo_tem_altura_de_linha_que_comporta_a_fonte_e_nao_corta_a_descendente(string cssClass)
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var title = new TextBlock { Text = "projeto Ajustes jogo Capítulo", Classes = { cssClass } };
                var window = new Window { Content = title };
                try
                {
                    window.Show();
                    title.Measure(Size.Infinity);
                    var natural = NaturalLineHeight(title);

                    Assert.True(title.LineHeight >= natural, $"{cssClass}: altura de linha {title.LineHeight} abaixo dos {natural} da fonte");
                    Assert.True(title.DesiredSize.Height >= natural, $"{cssClass}: o TextBlock mede {title.DesiredSize.Height} e a fonte pede {natural}");
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }
}
