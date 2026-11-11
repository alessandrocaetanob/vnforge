using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O conteúdo adulto (#631) fora da grade da Biblioteca: o desfoque conferido nos pixels de uma imagem com detalhe, a aba Privacidade dos Ajustes nos dois temas e a Ficha com o
/// retrato adulto nos três modos. Os PNGs <c>ajustes-privacidade</c> e <c>ficha-adulto-*</c> seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class AdultRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant ThemeOf(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    /// <summary>Uma capa de 240×320 com faixas verticais de 40 px, preto e branco: o detalhe que o borrão tem de apagar.</summary>
    private sealed class StripedSource(AdultMode mode) : IVndbImageSource
    {
        public event EventHandler? PrivacyChanged
        {
            add { }
            remove { }
        }

        public LibraryPrivacy Privacy => new(Adult: mode);

        public BitmapLease? TryGet(VndbImageRef image) => BitmapLease.Own(Decode());

        public void RevalidatePrivacy()
        {
        }

        public Task<BitmapLease?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken) => Task.FromResult<BitmapLease?>(BitmapLease.Own(Decode()));

        private static Bitmap Decode()
        {
            const int width = 240;
            const int height = 320;
            using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var frame = bitmap.Lock())
            {
                var pixels = new byte[frame.RowBytes * height];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var value = (byte)((x / 40) % 2 == 0 ? 0 : 255);
                        var o = (y * frame.RowBytes) + (x * 4);
                        pixels[o] = value;
                        pixels[o + 1] = value;
                        pixels[o + 2] = value;
                        pixels[o + 3] = 255;
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            return new Bitmap(stream);
        }
    }

    /// <summary>O maior salto de brilho entre dois pixels vizinhos na horizontal, longe da borda do controle.</summary>
    private static int SharpestStep(PixelProbe probe)
    {
        var worst = 0;
        for (var y = 8; y < probe.Height - 8; y++)
        {
            for (var x = 8; x < probe.Width - 9; x++)
            {
                worst = Math.Max(worst, Math.Abs(probe.At(x, y).R - probe.At(x + 1, y).R));
            }
        }

        return worst;
    }

    [Fact]
    public async Task O_borrao_apaga_o_detalhe_da_capa_adulta_e_o_mostrar_e_o_modo_Mostrar_devolvem_a_imagem_nitida()
    {
        await fixture.Session.Dispatch<int>(
            () =>
            {
                var previous = VndbImages.Source;
                var window = new Window { Width = 300, Height = 400 };
                try
                {
                    var adultCover = new VndbImageRef("https://t.vndb.org/cv/real.jpg", Sexual: 2);
                    VndbImages.Source = new StripedSource(AdultMode.Blur);
                    var cover = new Cover { Width = 192, Image = adultCover };
                    window.Content = cover;
                    window.Show();
                    ScreenRenderer.Settle(window);

                    Assert.True(cover.HasImage);
                    Assert.True(cover.IsBlurred);
                    var blurred = SharpestStep(PixelProbe.Capture(window, cover));

                    cover.Revealed = true;
                    ScreenRenderer.Settle(window);
                    Assert.False(cover.IsBlurred);
                    var sharp = SharpestStep(PixelProbe.Capture(window, cover));

                    Assert.True(sharp >= 200, $"a imagem nítida deveria ter a borda da faixa inteira; o maior salto foi {sharp}");
                    Assert.True(blurred <= 40, $"o borrão deveria suavizar a borda da faixa; o maior salto foi {blurred}");

                    // Uma capa sem marca adulta no mesmo modo Desfocar não é tocada.
                    cover.Revealed = false;
                    cover.Image = new VndbImageRef("https://t.vndb.org/cv/clean.jpg");
                    ScreenRenderer.Settle(window);
                    Assert.False(cover.IsBlurred);
                    Assert.True(SharpestStep(PixelProbe.Capture(window, cover)) >= 200);
                }
                finally
                {
                    window.Close();
                    VndbImages.Source = previous;
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_arte_do_cartao_Desfocar_usa_o_raio_de_16_px_e_o_gato_e_a_borda_da_lua_nao_ficam_legiveis()
    {
        await fixture.Session.Dispatch<int>(
            () =>
            {
                var art = new AdultSampleArt { Width = 96, Height = 128, Blurred = true };
                var window = new Window { Width = 200, Height = 200, Content = art };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);
                    Assert.Equal(16, Assert.IsType<BlurEffect>(art.GetVisualDescendants().OfType<Viewbox>().Single().Effect).Radius);
                    var blurred = SharpestStep(PixelProbe.Capture(window, art));

                    art.Blurred = false;
                    ScreenRenderer.Settle(window);
                    var sharp = SharpestStep(PixelProbe.Capture(window, art));

                    Assert.True(sharp >= 60, $"o desenho nítido deveria ter a borda da lua e do gato; o maior salto foi {sharp}");
                    Assert.True(blurred <= 12, $"com 16 px de desfoque a borda da lua e do gato não deveria sobrar; o maior salto foi {blurred}");
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_aba_Privacidade_mostra_os_tres_modos_com_o_padrao_marcado_e_o_cartao_clicado_grava(string themeName)
    {
        var theme = ThemeOf(themeName);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var file = LibrarySamples.Save(config.Path, LibrarySamples.Library());
                var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), clock: new FixedClock(LibrarySamples.Now), readTranslation: LibrarySamples.Translation, sheets: new LibrarySamples.Sheets());
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(LibrarySamples.Now));
                var vm = new MainWindowViewModel(start, library: library);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await library.LoadAsync();
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
                    vm.Settings.SelectedTab = SettingsViewModel.PrivacyTab;
                    ScreenRenderer.Settle(window);

                    var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                    var headers = tabs.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsVisible).Select(t => t.Header);
                    Assert.Equal(["IA e gasto", "Estilo", "Formatação", "Acentos", "Dicionários", "Aparência", "Privacidade"], headers);

                    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToList();
                    foreach (var expected in new[]
                             {
                                 "Vale para toda a Biblioteca, em qualquer projeto. Fica guardado com a sua lista de jogos.", "Mostrar", "Desfocar", "Esconder", "Padrão",
                                 "Capa, tags, traços e imagens aparecem como vêm do VNDB.", "Sem o jogo", "Some da grade, da lista e da busca.", "Pela linha de comando",
                                 "vnf library privacy --adult mostrar|desfocar|esconder", "18+",
                             })
                    {
                        Assert.True(texts.Contains(expected), $"falta '{expected}' em: {string.Join(" | ", texts)}");
                    }

                    var cards = tabs.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("option") && r.IsEffectivelyVisible).ToList();
                    Assert.Equal(["Mostrar", "Desfocar", "Esconder"], cards.Select(c => Avalonia.Automation.AutomationProperties.GetName(c)));
                    Assert.Equal([false, true, false], cards.Select(c => c.IsChecked == true));
                    // Os três cartões de 312 px com 12 de vão, terminando junto do cartão "Pela linha de comando", que é neutro como eles.
                    var xs = cards.Select(c => c.TranslatePoint(default, window)!.Value.X).ToList();
                    Assert.Equal([280.0, 604.0, 928.0], xs);
                    Assert.All(cards, c => Assert.Equal(312.0, c.Bounds.Width));
                    var command = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CommandCard");
                    Assert.Equal(280.0, command.TranslatePoint(default, window)!.Value.X);
                    Assert.Equal(960.0, command.Bounds.Width);
                    Assert.DoesNotContain("infobar", command.Classes);
                    var cardFill = (ISolidColorBrush)cards[0].GetVisualDescendants().OfType<Border>().First(b => b.Name == "Card").Background!;
                    Assert.Equal(cardFill.Color, ((ISolidColorBrush)command.Background!).Color);
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ajustes-privacidade", theme)));

                    var hide = cards[2];
                    var at = hide.TranslatePoint(new Point(hide.Bounds.Width / 2, hide.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(at, MouseButton.Left);
                    window.MouseUp(at, MouseButton.Left);
                    ScreenRenderer.Settle(window);

                    Assert.Equal(AdultMode.Hide, library.AdultMode);
                    Assert.Equal(AdultMode.Hide, file.Load().Privacy.Adult);
                    Assert.Equal([false, false, true], cards.Select(c => c.IsChecked == true));
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
    public async Task A_Ficha_borra_o_retrato_adulto_no_Desfocar_e_o_olho_revela_o_jogo_inteiro(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Ficha(theme, AdultMode.Blur, async (window, vm) =>
        {
            var page = window.GetVisualDescendants().OfType<GameSheetView>().Single(v => v.IsEffectivelyVisible);
            var portrait = page.GetVisualDescendants().OfType<Portrait>().First();
            Assert.True(portrait.IsBlurred);
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("1 tag de conteúdo adulto", texts);
            Assert.Contains("Mostrar", texts);
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-adulto-desfocar", theme)));

            var eye = page.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("sheet-pveil") && b.IsEffectivelyVisible);
            eye.Command!.Execute(eye.CommandParameter);
            ScreenRenderer.Settle(window);

            Assert.False(page.GetVisualDescendants().OfType<Portrait>().First().IsBlurred);
            Assert.DoesNotContain("1 tag de conteúdo adulto", page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.True(vm.Library.Reveal.IsRevealed(GameSheetSamples.Id));
            await Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_Ficha_no_Esconder_volta_ao_quadro_tracejado_e_no_Mostrar_traz_o_retrato_e_a_tag(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Ficha(theme, AdultMode.Hide, (window, vm) =>
        {
            var page = window.GetVisualDescendants().OfType<GameSheetView>().Single(v => v.IsEffectivelyVisible);
            Assert.Equal(5, page.GetVisualDescendants().OfType<Portrait>().Count(p => p.IsEffectivelyVisible));
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Equal(1, page.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("sheet-portrait-box") && b.IsEffectivelyVisible));
            Assert.Contains("Kenta Aizawa", texts);
            Assert.DoesNotContain("Mostrar", texts);
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-adulto-esconder", theme)));
            return Task.CompletedTask;
        });
        await Ficha(theme, AdultMode.Show, (window, vm) =>
        {
            var page = window.GetVisualDescendants().OfType<GameSheetView>().Single(v => v.IsEffectivelyVisible);
            var portrait = page.GetVisualDescendants().OfType<Portrait>().First();
            Assert.True(portrait.HasImage);
            Assert.False(portrait.IsBlurred);
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("sheet-portrait-box") && b.IsEffectivelyVisible);
            Assert.DoesNotContain("1 tag de conteúdo adulto", texts);
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ficha-adulto-mostrar", theme)));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Nos_Personagens_o_retrato_borrado_da_lista_e_da_pagina_mostra_o_olho_e_o_clique_revela_o_jogo()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var previous = VndbImages.Source;
                VndbImages.Source = new LibrarySamples.Images(new LibraryPrivacy(Adult: AdultMode.Blur));
                await CharactersSamples.SeedAsync(config.Path, matched: true, showSpoilers: true, TestContext.Current.CancellationToken, adultPortrait: true);
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(config.Path), new FixedClock(CharactersSamples.Now));
                var vm = new MainWindowViewModel(start);
                vm.Characters.ConfigDirectory = config.Path;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, DataContext = vm };
                try
                {
                    window.Show();
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Characters);
                    await vm.Characters.Loading;
                    ScreenRenderer.Settle(window);

                    var view = window.GetVisualDescendants().OfType<CharactersView>().Single(v => v.IsEffectivelyVisible);
                    Assert.True(view.GetVisualDescendants().OfType<Portrait>().First(p => p.IsEffectivelyVisible && p.Seed == "Hana Tsukino").IsBlurred);
                    var eye = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("rvb") && b.IsEffectivelyVisible);
                    Assert.Equal("Mostrar o retrato de Hana Tsukino", Avalonia.Automation.AutomationProperties.GetName(eye));
                    eye.Command!.Execute(eye.CommandParameter);
                    ScreenRenderer.Settle(window);

                    Assert.True(vm.Library.Reveal.IsRevealed(CharactersSamples.Moon));
                    Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("rvb") && b.IsEffectivelyVisible);
                    Assert.False(view.GetVisualDescendants().OfType<Portrait>().First(p => p.IsEffectivelyVisible && p.Seed == "Hana Tsukino").IsBlurred);

                    vm.Library.Reveal.Clear();
                    vm.Characters.OpenCharacterCommand.Execute(vm.Characters.Items.Single(i => i.Name == "Hana Tsukino"));
                    ScreenRenderer.Settle(window);

                    Assert.True(view.GetVisualDescendants().OfType<Portrait>().Single(p => p.IsEffectivelyVisible && p.Seed == "Hana Tsukino").IsBlurred);
                    eye = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("rvb") && b.IsEffectivelyVisible);
                    eye.Command!.Execute(eye.CommandParameter);
                    ScreenRenderer.Settle(window);

                    Assert.False(view.GetVisualDescendants().OfType<Portrait>().Single(p => p.IsEffectivelyVisible && p.Seed == "Hana Tsukino").IsBlurred);
                    Assert.True(vm.Library.Reveal.IsRevealed(CharactersSamples.Moon));
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

    [Fact]
    public async Task Nos_Personagens_o_clique_e_o_teclado_no_olho_so_revelam_e_nao_abrem_a_pagina_do_personagem()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var previous = VndbImages.Source;
                VndbImages.Source = new LibrarySamples.Images(new LibraryPrivacy(Adult: AdultMode.Blur));
                await CharactersSamples.SeedAsync(config.Path, matched: true, showSpoilers: true, TestContext.Current.CancellationToken, adultPortrait: true);
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(config.Path), new FixedClock(CharactersSamples.Now));
                var vm = new MainWindowViewModel(start);
                vm.Characters.ConfigDirectory = config.Path;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, DataContext = vm };
                try
                {
                    window.Show();
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Characters);
                    await vm.Characters.Loading;
                    ScreenRenderer.Settle(window);

                    Button Eye() => window.GetVisualDescendants().OfType<CharactersView>().Single(v => v.IsEffectivelyVisible)
                        .GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("rvb") && b.IsEffectivelyVisible);

                    var eye = Eye();
                    var at = eye.TranslatePoint(new Point(eye.Bounds.Width / 2, eye.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(at, MouseButton.Left);
                    window.MouseUp(at, MouseButton.Left);
                    ScreenRenderer.Settle(window);

                    Assert.True(vm.Library.Reveal.IsRevealed(CharactersSamples.Moon));
                    Assert.Null(vm.Characters.Page);

                    vm.Library.Reveal.Clear();
                    ScreenRenderer.Settle(window);
                    Eye().Focus();
                    window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                    ScreenRenderer.Settle(window);

                    Assert.True(vm.Library.Reveal.IsRevealed(CharactersSamples.Moon));
                    Assert.Null(vm.Characters.Page);
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

    private async Task Ficha(ThemeVariant theme, AdultMode mode, Func<MainWindow, MainWindowViewModel, Task> verificar)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var previous = VndbImages.Source;
                VndbImages.Source = new LibrarySamples.Images(new LibraryPrivacy(Adult: mode));
                await GameSheetSamples.SeedAsync(config.Path, translate: true, TestContext.Current.CancellationToken, GameSheetSamples.AdultCatalog());
                var held = new LibraryGame("g1", "C:\\Jogos\\Amaoto", null, LibraryEngine.Unknown, "Amaoto no Hakoniwa", null, GameSheetSamples.Now) { VndbId = GameSheetSamples.Id };
                new LibraryFile(config.Path).Save(new GameLibrary([held], new LibraryPrivacy(Adult: mode)));
                ProjectFolder.Open(demoFolder.Root).SaveVndbLink(new VndbLink(GameSheetSamples.Id, "Amaoto no Hakoniwa", "雨音の箱庭", null, []));
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(GameSheetSamples.Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: Core.Demo.DemoProject.Label) };
                vm.Sheet.ConfigDirectory = config.Path;
                vm.Sheet.CatalogFactory = GameSheetSamples.Catalog;
                vm.Sheet.TranslatorFactory = GameSheetSamples.Translator;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    await vm.Sheet.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Sheet);
                    await vm.Dashboard.ReloadAsync();
                    ScreenRenderer.Settle(window);

                    await verificar(window, vm);
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
