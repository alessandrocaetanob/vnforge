using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace VNForge.App.Tests.Rendering;

public sealed class PixelProbeTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Fact]
    public Task Controle_fora_do_quadro_lanca_excecao_em_vez_de_ler_fora_do_buffer() => fixture.Session.Dispatch<int>(
        () =>
        {
            var box = new Border { Width = 50, Height = 50, Background = Brushes.Red };
            Canvas.SetLeft(box, 10);
            Canvas.SetTop(box, 80);
            var window = new Window { Width = 200, Height = 100, RequestedThemeVariant = ThemeVariant.Dark, Content = new Canvas { Children = { box } } };
            try
            {
                window.Show();
                ScreenRenderer.Settle(window);

                var erro = Assert.Throws<InvalidOperationException>(() => PixelProbe.Capture(window, box));
                Assert.Contains("10,80 50×50", erro.Message);
                Assert.Contains("200×100", erro.Message);
            }
            finally
            {
                window.Close();
            }

            return Task.FromResult(0);
        },
        TestContext.Current.CancellationToken);

    [Fact]
    public Task Controle_dentro_do_quadro_devolve_os_pixels() => fixture.Session.Dispatch<int>(
        () =>
        {
            var box = new Border { Width = 50, Height = 50, Background = Brushes.Red };
            Canvas.SetLeft(box, 10);
            Canvas.SetTop(box, 20);
            var window = new Window { Width = 200, Height = 100, RequestedThemeVariant = ThemeVariant.Dark, Content = new Canvas { Children = { box } } };
            try
            {
                window.Show();
                ScreenRenderer.Settle(window);

                var probe = PixelProbe.Capture(window, box);
                Assert.Equal((50, 50), (probe.Width, probe.Height));
                Assert.True(probe.Near(25, 25, Color.FromRgb(255, 0, 0), 2));
            }
            finally
            {
                window.Close();
            }

            return Task.FromResult(0);
        },
        TestContext.Current.CancellationToken);
}
