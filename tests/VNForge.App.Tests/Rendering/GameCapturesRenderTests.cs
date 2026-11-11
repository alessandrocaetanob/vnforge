using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>A aba Jogar (#553) montada de verdade no shell, nos dois temas, a 1440×1000 como o quadro: seis capturas com a data, as notas e o cartão "Na tradução". Os PNGs <c>jogar-capturas-*</c> seguem <c>VNFORGE_RENDER_DIR</c>.</summary>
public sealed class GameCapturesRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 21, 14, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static WindowImage Scene(int hue)
    {
        const int width = 320;
        const int height = 200;
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                bgra[at] = (byte)(40 + (hue * 7 % 80) + (y / 2));
                bgra[at + 1] = (byte)(60 + (hue * 13 % 60) + (x / 4));
                bgra[at + 2] = (byte)(90 + (hue * 29 % 100));
                bgra[at + 3] = 255;
            }
        }

        return new WindowImage(width, height, bgra);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Jogar_mostra_as_capturas_com_a_data_as_notas_e_o_caminho_para_o_Editor(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var data = new TempFolder();
                var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 19, 48, 0, TimeSpan.Zero));
                var captures = new GameCapturesFile(data.SubFolder("captures"), clock);
                var game = new LibraryGame(
                    "g1a2b3c4d", GameLibrary.NormalizeFolder(data.SubFolder("jogo")), "jogo.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Amaoto no Hakoniwa", "雨音の箱庭", Now)
                {
                    ProjectPath = GameLibrary.NormalizeFolder(demoFolder.Root),
                };
                var taken = new[] { (4, 19, 48), (4, 20, 20), (6, 22, 55), (6, 23, 10), (8, 21, 14), (8, 21, 31) };
                for (var i = 0; i < taken.Length; i++)
                {
                    clock.SetUtcNow(new DateTimeOffset(2026, 11, taken[i].Item1, taken[i].Item2, taken[i].Item3, 0, TimeSpan.Zero));
                    captures.Save(game.Id, Scene(i + 1));
                }

                var notes = new GameNotesFile(config.SubFolder("notas"));
                notes.Update(all => all.Set(game.Id, "Capítulo 4: a caixa de música toca só quando mentem.\nVoltar na rota da Aoi depois do festival.\n\nPara a tradução: “guarda-chuva” é recorrente, usar sempre o mesmo termo no Glossário.\nEm 08/11 a captura das 21:31 tem a placa do portão, com texto na imagem.", Now.AddSeconds(-4)));

                var library = new LibraryViewModel(LibrarySamples.Save(config.SubFolder("biblioteca"), new GameLibrary([game])), new FakeFolderPicker(null), action => action(), clock: new FixedClock(Now));
                await library.LoadAsync();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, library: library) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                vm.Play.CapturesFile = captures;
                vm.Play.NotesFile = notes;
                vm.Play.Clock = new FixedClock(Now);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);
                    ScreenRenderer.Settle(window);

                    var page = Assert.Single(window.GetVisualDescendants().OfType<GameCapturesView>(), v => v.IsEffectivelyVisible);
                    Assert.False(page.FindControl<Control>("NoGame")!.IsEffectivelyVisible);
                    var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                    foreach (var expected in new[] { "Capturas", "Abrir a pasta", "Notas do jogo", "Salvo há 4 segundos", "Na tradução", "Ver no Editor", "04/11 19:48", "08/11 21:14" })
                    {
                        Assert.Contains(expected, texts);
                    }

                    var shots = page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("shot") && b.IsEffectivelyVisible).ToList();
                    Assert.Equal(6, shots.Count);
                    Assert.Equal(3, shots.Select(s => Math.Round(s.TranslatePoint(default, page)!.Value.X)).Distinct().Count());
                    Assert.All(shots, s => Assert.InRange(s.Bounds.Width / s.Bounds.Height, 1.55, 1.65));
                    // As miniaturas são decodificadas fora da thread da tela e chegam um pouco depois do layout.
                    var thumbnails = page.GetVisualDescendants().OfType<FileThumbnail>().Where(i => i.IsEffectivelyVisible).ToList();
                    Assert.Equal(6, thumbnails.Count);
                    for (var wait = 0; wait < 100 && thumbnails.Any(t => t.Source is null); wait++)
                    {
                        await Task.Delay(50, TestContext.Current.CancellationToken);
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    }

                    Assert.All(thumbnails, t => Assert.NotNull(t.Source));
                    ScreenRenderer.Settle(window);
                    Assert.False(page.FindControl<Control>("BlackBanner")!.IsEffectivelyVisible);
                    Assert.Equal(352, page.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("notes-field")).Bounds.Width + 2 * 24 + 2, 1);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("jogar-capturas", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
