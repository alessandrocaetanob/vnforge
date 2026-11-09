using Avalonia;

namespace VNForge.App.Tests;

/// <summary>Os controles padrão do Fluent acompanham os raios do design system v3 (radius-sm e radius-lg).</summary>
public class FluentRadiiHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Fact]
    public Task Raios_do_Fluent_vem_dos_tokens() => fixture.Session.Dispatch<int>(
        () =>
        {
            var app = Application.Current!;
            Assert.True(app.TryGetResource("ControlCornerRadius", null, out var control));
            Assert.True(app.TryGetResource("Vnf.RadiusSm", null, out var sm));
            Assert.Equal(new CornerRadius(8), control);
            Assert.Equal(sm, control);
            Assert.True(app.TryGetResource("OverlayCornerRadius", null, out var overlay));
            Assert.True(app.TryGetResource("Vnf.RadiusLg", null, out var lg));
            Assert.Equal(new CornerRadius(16), overlay);
            Assert.Equal(lg, overlay);
            return Task.FromResult(0);
        },
        TestContext.Current.CancellationToken);
}
