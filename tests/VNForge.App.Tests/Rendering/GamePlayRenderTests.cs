using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>A aba Jogar com o tempo de jogo (#549) montada de verdade no shell, nos dois temas, a 1440×1000: as sessões, o total e o gráfico por semana. Os PNGs <c>jogar-tempo-*</c> seguem <c>VNFORGE_RENDER_DIR</c>.</summary>
public sealed class GamePlayRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static PlaySession Session(DateTime day, int hour, int minute, int minutes, bool viaLocaleEmulator = true)
    {
        var started = new DateTimeOffset(day.Date.AddHours(hour).AddMinutes(minute));
        return new PlaySession(PlaySession.NewId(), started, started.AddMinutes(minutes), TimeSpan.FromMinutes(minutes), ViaLocaleEmulator: viaLocaleEmulator);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Jogar_mostra_as_sessoes_o_tempo_total_o_grafico_por_semana_e_os_interruptores(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var data = new TempFolder();
                var now = DateTimeOffset.Now;
                var game = new LibraryGame(
                    "g1a2b3c4d", GameLibrary.NormalizeFolder(data.SubFolder("jogo")), "Game.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Amaoto no Hakoniwa", "雨音の箱庭", now)
                {
                    ProjectPath = GameLibrary.NormalizeFolder(demoFolder.Root),
                    RequiresJapaneseLocale = true,
                };
                var folder = config.SubFolder("biblioteca");
                var playLog = new PlayLogFile(folder);
                foreach (var (daysAgo, hour, minute, minutes) in new[] { (0, 21, 14, 112), (1, 20, 2, 130), (4, 22, 40, 48), (6, 19, 30, 185), (12, 20, 0, 90), (20, 18, 30, 150), (33, 21, 0, 60), (47, 20, 15, 120), (61, 19, 0, 45) })
                {
                    playLog.Record(game.Id, Session(now.Date.AddDays(-daysAgo), hour, minute, minutes));
                }

                var library = new LibraryViewModel(LibrarySamples.Save(folder, new GameLibrary([game])), new FakeFolderPicker(null), action => action(), readTranslation: _ => null, playLog: playLog);
                await library.LoadAsync();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(now));
                var vm = new MainWindowViewModel(start, library: library) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);
                    ScreenRenderer.Settle(window);

                    var page = Assert.Single(window.GetVisualDescendants().OfType<GameCapturesView>(), v => v.IsEffectivelyVisible);
                    var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                    foreach (var expected in new[] { "Pronto para jogar", "Abrir pelo Locale Emulator", "Abre um lançador antes do jogo", "Sessões", "Tempo de jogo", "Último jogado", "15 h 40 min", "Hoje, 21:14", "1 h 52 min", "Locale Emulator" })
                    {
                        Assert.Contains(expected, texts);
                    }

                    var bars = page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("play-bar") && b.IsEffectivelyVisible).ToList();
                    Assert.Equal(PlayHistory.Weeks, bars.Count);
                    Assert.Equal(1, bars.Count(b => b.Classes.Contains("current")));
                    Assert.Contains(page.GetVisualDescendants().OfType<ToggleSwitch>(), s => s.Name == "LocaleEmulatorSwitch" && s.IsChecked == true);
                    Assert.Contains(page.GetVisualDescendants().OfType<ToggleSwitch>(), s => s.Name == "OpensLauncherSwitch" && s.IsChecked == false);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("jogar-tempo", theme)));
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
