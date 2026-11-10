using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A lista do VNDB (#550) montada de verdade nos dois temas, a 1440×1000 como o quadro: a aba VNDB de Ajustes com o diálogo "A lista mudou nos dois lados" e o cartão
/// "Na sua lista do VNDB" na gaveta da Biblioteca. Os PNGs <c>vndb-*</c> seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class VndbListRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = LibrarySamples.Now;

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private sealed class Vault : IVndbTokenStore
    {
        private string? token = FakeVndbCatalog.ValidToken;

        public string? Load() => token;

        public void Save(string value) => token = value;

        public bool Delete() => true;
    }

    private static List<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).Where(t => t.Length > 0)];

    private static (VndbListService Service, LibraryFile File) Montar(string config, VndbListEntry? remote)
    {
        var amaoto = LibrarySamples.Games()[0];
        var list = new LibraryListState(amaoto.VndbId!, new LibraryListFields
        {
            Labels = [LibraryListLabels.Named(LibraryListLabels.Playing)],
            Vote = 80,
            Started = new DateOnly(2026, 10, 3),
            Notes = "Capítulo 4 melhora muito. Voltar na rota da Aoi.",
        }, Now.AddMinutes(-3))
        {
            Base = new LibraryListFields
            {
                Labels = [LibraryListLabels.Named(LibraryListLabels.Playing)],
                Vote = 80,
                Started = new DateOnly(2026, 10, 3),
                Notes = "Capítulo 4 melhora muito. Voltar na rota da Aoi.",
            },
            SyncedAt = Now.AddMinutes(-3),
            OnVndb = true,
        };
        var games = LibrarySamples.Games().Select(g => g.Id == amaoto.Id ? g with { List = list } : g);
        var file = LibrarySamples.Save(config, LibrarySamples.Library(games));
        var fake = new FakeVndbCatalog(list: remote is null ? [] : [remote], time: new FixedClock(Now));
        return (new VndbListService(new Vault(), fake, file, new FixedClock(Now), _ => null), file);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Ajustes_VNDB_mostra_o_token_o_que_ele_permite_e_o_dialogo_dos_dois_lados(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                // O VNDB mudou o voto depois da última sincronização e o app também: o conflito abre o diálogo.
                var (service, file) = Montar(config.Path, new VndbListEntry(LibrarySamples.Games()[0].VndbId!, "Amaoto", Now, Now.AddMinutes(-1), null, 90, new DateOnly(2026, 10, 3), null, "Capítulo 4 melhora muito. Voltar na rota da Aoi.", [LibraryListLabels.Named(LibraryListLabels.Playing)]));
                service.Edit(LibrarySamples.Games()[0].Id, new VndbListUpdate { Vote = 70 });
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(config.SubFolder("ajustes"))) { Vndb = new VndbConnectionViewModel(service, new FixedClock(Now)) };
                var vm = new MainWindowViewModel(start, settings: settings);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
                    vm.Settings.SelectedTab = 7;
                    await settings.Vndb!.RefreshAsync();
                    ScreenRenderer.Settle(window);

                    var texts = Texts(window);
                    foreach (var expected in new[] { "VNDB", "Token pessoal", "••••-••••-••••-" + FakeVndbCatalog.ValidToken[^4..], "Conectado como fake", "Ler a lista", "Gravar na lista (listwrite)", "Trocar token", "Remover do cofre", "O que sincroniza", "Sincronizar agora" })
                    {
                        Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
                    }

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("vndb-ajustes", theme)));

                    await settings.Vndb.SyncNowCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);
                    Assert.True(settings.Vndb.IsConflictsOpen);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("vndb-conflito", theme)));
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
    public async Task Gaveta_mostra_a_lista_do_VNDB_do_jogo(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var previous = VndbImages.Source;
                VndbImages.Source = new LibrarySamples.Images();
                var (service, file) = Montar(config.Path, null);
                var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), clock: new FixedClock(Now), readTranslation: LibrarySamples.Translation, sheets: new LibrarySamples.Sheets()) { ListService = service };
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, library: library);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await library.LoadAsync();
                    library.Select(library.Cards.Single(c => c.Title == "Amaoto no Hakoniwa"));
                    ScreenRenderer.Settle(window);

                    var texts = Texts(window);
                    foreach (var expected in new[] { "Na sua lista do VNDB", "Sincronizada há 3 min", "Jogando", "Terminado", "Abandonado", "Quero jogar", "8 de 10", "03/10/2026", "ainda não terminou", "Marcar como terminado", "Abrir no VNDB" })
                    {
                        Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
                    }

                    var chips = window.GetVisualDescendants().OfType<FilterChip>().Where(c => c.Label is "Jogando" or "Terminado").ToList();
                    Assert.Equal([true, false], chips.Select(c => c.IsChecked == true));
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("vndb-gaveta", theme)));
                }
                finally
                {
                    window.Close();
                    VndbImages.Source = previous;
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
