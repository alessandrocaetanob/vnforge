using VNForge.Core.Demo;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.App.Tests.Demo;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza o Início com os três projetos do artboard 3 do canvas v2 (Minha VN, Teste YPF, Segunda VN) para pôr ao lado dele no PR.
/// Os arquivos são <c>inicio-artboard-escuro.png</c> e <c>inicio-artboard-claro.png</c> (Minha VN selecionado) e
/// <c>inicio-artboard-sem-suporte-escuro.png</c> e <c>-claro.png</c> (Segunda VN selecionado, motor sem suporte).
/// </summary>
public sealed class StartRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_Inicio_com_os_projetos_do_artboard(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            () =>
            {
                var start = new StartViewModel(new RecentProjectsFile(Path.Combine(Path.GetTempPath(), "vnforge-app-tests", "sem-recentes")), new FakeFolderPicker(null), new FixedClock(Now));
                var items = Items();
                foreach (var item in items)
                {
                    start.Recents.Add(item);
                }

                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = new MainWindowViewModel(start) };
                try
                {
                    window.Show();
                    foreach (var (selected, name) in new[] { (items[0], "inicio-artboard"), (items[2], "inicio-artboard-sem-suporte") })
                    {
                        start.Selected = selected;
                        ScreenRenderer.Settle(window);
                        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
                        ScreenRenderer.Save(window, path);

                        using var png = new Bitmap(path);
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Os três projetos do artboard: nomes, caminhos, números e datas dele; o último build e o script de "Continuar" são os do Minha VN.</summary>
    private static RecentProjectItem[] Items()
    {
        var minha = new ProjectSummary(
            DemoProject.Name, @"D:\Traducoes\minha-vn-ptbr", "yuris", 500, "cp1252", DemoProject.ModelId, DemoProject.ScriptCount, DemoProject.Counts,
            new LastBuild(Now.AddDays(-2), 14), DemoProject.CurrentScript, "medium");
        var teste = new ProjectSummary(
            "Teste YPF", @"D:\Traducoes\teste-ypf", "yuris", 500, "fold", "claude-haiku-5-5", 9, new StatusCounts(1_204, 0, 0, 0, 0),
            new LastBuild(Now.AddDays(-3), 0), "yst00001", "medium");
        var segunda = new ProjectSummary(
            "Segunda VN", @"D:\Jogos\SegundaVN", "kirikiri", null, "fold", "claude-opus-5-5", 0, new StatusCounts(0, 0, 0, 0, 0),
            null, null, "medium");
        return
        [
            RecentProjectItem.FromSummary(minha, Now.AddDays(-1), Now),
            RecentProjectItem.FromSummary(teste, Now.AddDays(-3), Now),
            RecentProjectItem.FromSummary(segunda, new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero), Now),
        ];
    }
}
