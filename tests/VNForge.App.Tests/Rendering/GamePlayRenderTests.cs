using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
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
                    Assert.Equal(4, page.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "Rows").ItemCount);
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

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Jogar_com_a_partida_em_curso_mostra_Jogando_agora_o_relogio_e_o_botao_desabilitado(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var data = new TempFolder();
                var now = DateTimeOffset.Now;
                var gameFolder = data.SubFolder("jogo");
                File.WriteAllText(Path.Combine(gameFolder, "Game.exe"), "x");
                var emulator = data.SubFolder("LocaleEmulator");
                File.WriteAllText(Path.Combine(emulator, "LEProc.exe"), "x");
                var game = new LibraryGame(
                    "g1a2b3c4d", GameLibrary.NormalizeFolder(gameFolder), "Game.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Amaoto no Hakoniwa", "雨音の箱庭", now)
                {
                    ProjectPath = GameLibrary.NormalizeFolder(demoFolder.Root),
                    RequiresJapaneseLocale = true,
                };
                var folder = config.SubFolder("biblioteca");
                var playLog = new PlayLogFile(folder);
                foreach (var (daysAgo, hour, minute, minutes) in new[] { (1, 20, 2, 130), (4, 22, 40, 48), (6, 19, 30, 185), (12, 20, 0, 90), (20, 18, 30, 150), (33, 21, 0, 60) })
                {
                    playLog.Record(game.Id, Session(now.Date.AddDays(-daysAgo), hour, minute, minutes));
                }

                var file = LibrarySamples.Save(folder, new GameLibrary([game], localeEmulatorPath: emulator));

                // A partida abriu há 42 min 18 s e o processo falso nunca sai: o relógio do jogador é o da Biblioteca, e a aba lê dele.
                var clock = new FakeTimeProvider(DateTimeOffset.UtcNow - new TimeSpan(0, 42, 18));
                var options = new PlayTrackingOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10));
                var library = new LibraryViewModel(
                    file, new FakeFolderPicker(null), action => action(), clock: clock, readTranslation: _ => null,
                    player: new GamePlayer(new StillRunningLauncher(), playLog, clock, options), playLog: playLog);
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
                    _ = library.PlayCommand.ExecuteAsync(library.Cards.Single());
                    while (library.PlayingId is null && library.ErrorMessage is null)
                    {
                        await Task.Delay(10, TestContext.Current.CancellationToken);
                    }

                    Assert.Null(library.ErrorMessage);

                    vm.Play.Session!.Tick();
                    ScreenRenderer.Settle(window);

                    var page = Assert.Single(window.GetVisualDescendants().OfType<GameCapturesView>(), v => v.IsEffectivelyVisible);
                    var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToList();
                    Assert.Contains("Jogando agora", texts);
                    Assert.Contains(texts, t => t.StartsWith("Aberto às ", StringComparison.Ordinal) && t.EndsWith(" pelo Locale Emulator · o app conta pelo processo Game.exe", StringComparison.Ordinal));
                    Assert.Contains(texts, t => t.StartsWith("00:42:", StringComparison.Ordinal));
                    Assert.DoesNotContain("Pronto para jogar", texts);
                    // A partida abriu há 42 min: passada a meia-noite (hora local) ela é de "Ontem".
                    var liveDay = (DateTimeOffset.UtcNow - new TimeSpan(0, 42, 18)).ToLocalTime().Date == DateTime.Now.Date ? "Hoje, " : "Ontem, ";
                    Assert.Contains(texts, t => t.StartsWith(liveDay, StringComparison.Ordinal));
                    Assert.Equal(4, page.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "Rows").ItemCount);
                    var card = page.GetVisualDescendants().OfType<GamePlayView>().Single();
                    Assert.False(card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PlayButton").IsEffectivelyVisible);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("jogar-jogando", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private sealed class StillRunningLauncher : IGameLauncher
    {
        public IGameProcess Start(GameLaunch launch) => new Process();

        private sealed class Process : IGameProcess
        {
            public GameProcessState Poll() => GameProcessState.Running;

            public void Dispose()
            {
            }
        }
    }
}
