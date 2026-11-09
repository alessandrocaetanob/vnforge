using VNForge.App.Services;
using VNForge.Core.Images;

namespace VNForge.App.Tests;

/// <summary>O compositor de verdade (Skia headless): apaga a área, escreve o texto e devolve um PNG que o app lê de volta.</summary>
public class ImageComposerHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Width = 200;
    private const int Height = 100;

    private static RasterPicture Background()
    {
        var bgra = new byte[Width * Height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (180, 120, 60, 255);
        }

        // O "texto original": pixels escuros na área que vai ser apagada.
        for (var y = 40; y < 60; y++)
        {
            for (var x = 60; x < 140; x++)
            {
                var at = ((y * Width) + x) * 4;
                (bgra[at], bgra[at + 1], bgra[at + 2]) = (0, 0, 0);
            }
        }

        return new RasterPicture(Width, Height, bgra);
    }

    private static ImageEdit Edit(string text) =>
        new("img.ypf", "bg/portao.png", "ab", Width, Height, new ImageRegion(50, 30, 100, 40), ImageEraseModes.Surrounding, null, text, "Noto Sans", 20, true, "#ffffff", null, true);

    private static bool IsBackground(RasterPicture picture, int x, int y)
    {
        var at = ((y * picture.Width) + x) * 4;
        return Math.Abs(picture.Bgra[at] - 180) <= 2 && Math.Abs(picture.Bgra[at + 1] - 120) <= 2 && Math.Abs(picture.Bgra[at + 2] - 60) <= 2;
    }

    [Fact]
    public async Task WithoutTextTheAreaIsErasedWithTheSurroundingColourAndTheRestIsUntouched()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var composer = new AvaloniaImageComposer();
                var result = composer.Decode(composer.Render(Background(), Edit(string.Empty)));

                Assert.Equal((Width, Height), (result.Width, result.Height));
                Assert.True(IsBackground(result, 100, 50), "a área do texto original continua escura");
                Assert.True(IsBackground(result, 5, 5));
                Assert.True(IsBackground(result, 195, 95));
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheTextIsWrittenInsideTheAreaAndNothingIsDrawnOutsideIt()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var composer = new AvaloniaImageComposer();
                var result = composer.Decode(composer.Render(Background(), Edit("Portão")));

                var inside = Enumerable.Range(30, 40).SelectMany(y => Enumerable.Range(50, 100).Select(x => (x, y))).Count(p => !IsBackground(result, p.x, p.y));
                var outside = Enumerable.Range(0, Height).SelectMany(y => Enumerable.Range(0, Width).Select(x => (x, y)))
                    .Count(p => (p.x < 50 || p.x >= 150 || p.y < 30 || p.y >= 70) && !IsBackground(result, p.x, p.y));
                Assert.True(inside > 30, $"só {inside} pixels de texto na área");
                Assert.Equal(0, outside);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MeasureGrowsWithTheTextAndTheFontSize()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var composer = new AvaloniaImageComposer();
                var small = composer.Measure(Edit("Portão"));
                var longer = composer.Measure(Edit("Portão do colégio"));
                var bigger = composer.Measure(Edit("Portão") with { FontSize = 40 });

                Assert.True(longer.Width > small.Width);
                Assert.True(bigger.Height > small.Height);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SomethingThatIsNotAnImageIsRefused()
    {
        await fixture.Session.Dispatch(
            () => Assert.Throws<InvalidDataException>(() => new AvaloniaImageComposer().Decode([1, 2, 3, 4])),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheEmbeddedFontsComeFirstInTheChoices()
    {
        await fixture.Session.Dispatch(
            () => Assert.Equal("Zen Kaku Gothic New", new AvaloniaImageComposer().FontFamilies[0]),
            TestContext.Current.CancellationToken);
    }
}
