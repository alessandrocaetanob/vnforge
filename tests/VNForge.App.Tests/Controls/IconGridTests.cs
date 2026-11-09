using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Tests.Controls;

/// <summary>Os ícones saem na escala do SVG do quadro (#580): a grade de 20 vale como limite do traçado, então um glifo em caixa de 16 ocupa 12,8 e não 16.</summary>
public class IconGridTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData("Icon.Play")]
    [InlineData("Icon.Check")]
    [InlineData("Icon.Eye")]
    [InlineData("Icon.Lightbulb")]
    [InlineData("Icon.Dismiss")]
    [InlineData("Icon.ChevronDown")]
    public async Task Icone_tem_a_grade_de_20_como_limite(string key)
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window();
                try
                {
                    Assert.True(window.TryFindResource(key, out var found));
                    Assert.Equal(new Rect(0, 0, 20, 20), ((Geometry)found!).Bounds);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }
}
