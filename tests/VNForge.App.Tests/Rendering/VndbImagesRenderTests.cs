using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Bible;
using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Capas e retratos do VNDB (#580): o <see cref="Portrait"/> e o <see cref="Cover"/> carregam do cache, mostram o marcador sem imagem ou com a privacidade escondendo-a,
/// e entram no resultado da busca, na ficha do VNDB do assistente e nos chips da Bíblia. O downloader é falso: nenhum teste abre rede. Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class VndbImagesRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private static readonly VndbImageRef KentaFace = new("https://t.vndb.org/ch/12/3412.jpg");
    private static readonly VndbImageRef AdultFace = new("https://t.vndb.org/ch/12/9999.jpg", Sexual: 2);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    /// <summary>Um PNG sintético de <paramref name="width"/> por <paramref name="height"/>: degradê na matiz dada, para ver que a imagem foi cortada e posta no lugar.</summary>
    private static byte[] Png(int width, int height, int hue)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            var pixels = new byte[frame.RowBytes * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var mix = (double)y / height;
                    var o = (y * frame.RowBytes) + (x * 4);
                    var inFace = Math.Pow(x - (width / 2.0), 2) + Math.Pow(y - (height * 0.35), 2) < Math.Pow(width * 0.22, 2);
                    var color = inFace ? Hsl(hue + 20, 0.5, 0.8) : Hsl(hue, 0.7, 0.65 - (0.4 * mix));
                    pixels[o] = color.B;
                    pixels[o + 1] = color.G;
                    pixels[o + 2] = color.R;
                    pixels[o + 3] = 255;
                }
            }

            Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static Avalonia.Media.Color Hsl(double hue, double saturation, double lightness) =>
        new Avalonia.Media.HslColor(1, ((hue % 360) + 360) % 360, saturation, lightness).ToRgb();

    private sealed class SyntheticDownloader : IImageDownloader
    {
        public List<Uri> Requested { get; } = [];

        public Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
        {
            lock (Requested)
            {
                Requested.Add(url);
            }

            var isCover = url.AbsolutePath.Contains("/cv", StringComparison.Ordinal);
            var hue = (int)(url.AbsolutePath.Aggregate(7, (h, c) => (h * 31) + c) % 360);
            return Task.FromResult<byte[]?>(isCover ? Png(120, 160, Math.Abs(hue)) : Png(256, 300, Math.Abs(hue)));
        }
    }

    private static async Task WaitForImages(Window window, int expected)
    {
        for (var i = 0; i < 200; i++)
        {
            ScreenRenderer.Settle(window);
            var shown = window.GetVisualDescendants().OfType<VndbImageControl>().Count(c => c.IsEffectivelyVisible && c.HasImage);
            if (shown >= expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"as imagens não chegaram: {window.GetVisualDescendants().OfType<VndbImageControl>().Count(c => c.HasImage)} de {expected}");
    }

    private static void Snapshot(Window window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    private sealed class Scope(TempFolder folder, SyntheticDownloader downloader, VndbImageSource source) : IDisposable
    {
        public SyntheticDownloader Downloader { get; } = downloader;

        public TempFolder Temp { get; } = folder;

        public static Scope Start(Func<LibraryPrivacy>? privacy = null)
        {
            var temp = new TempFolder();
            var downloader = new SyntheticDownloader();
            var source = new VndbImageSource(new VndbImageCache(Path.Combine(temp.Path, "imagens"), downloader, privacy));
            VndbImages.Source = source;
            return new Scope(temp, downloader, source);
        }

        public void Dispose()
        {
            VndbImages.Source = null;
            source.Dispose();
            Temp.Dispose();
        }
    }

    // ----- Os controles -----

    [Fact]
    public async Task Os_tamanhos_do_retrato_e_da_capa_seguem_os_tokens_do_quadro()
    {
        await fixture.Session.Dispatch<int>(
            () =>
            {
                var panel = new StackPanel
                {
                    Children =
                    {
                        new Portrait { Size = PortraitSize.Sm },
                        new Portrait { Size = PortraitSize.Md },
                        new Portrait { Size = PortraitSize.Lg },
                        new Cover(),
                        new Cover { Width = 72 },
                    },
                };
                var window = new Window { Width = 400, Height = 600, Content = panel };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);
                    var sizes = panel.Children.Select(c => (c.Bounds.Width, c.Bounds.Height)).ToArray();

                    Assert.Equal((28.0, 28.0), sizes[0]);
                    Assert.Equal((56.0, 56.0), sizes[1]);
                    Assert.Equal((64.0, 64.0), sizes[2]);
                    Assert.Equal(44.0, sizes[3].Width);
                    Assert.InRange(sizes[3].Height, (44.0 * 4 / 3) - 1, (44.0 * 4 / 3) + 1);
                    Assert.Equal((72.0, 96.0), sizes[4]);
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task O_retrato_carrega_do_cache_baixando_uma_vez_e_o_que_a_privacidade_esconde_fica_no_marcador()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var scope = Scope.Start(() => new LibraryPrivacy());
                var shown = new Portrait { Size = PortraitSize.Md, Image = KentaFace, Seed = "Kenta" };
                var again = new Portrait { Size = PortraitSize.Sm, Image = KentaFace, Seed = "Kenta" };
                var hidden = new Portrait { Size = PortraitSize.Md, Image = AdultFace, Seed = "Proibida" };
                var none = new Portrait { Size = PortraitSize.Md, Seed = "Sem imagem" };
                var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { shown, again, hidden, none } } };
                try
                {
                    window.Show();
                    await WaitForImages(window, 2);

                    Assert.True(shown.HasImage && again.HasImage);
                    Assert.False(hidden.HasImage);
                    Assert.False(none.HasImage);
                    Assert.Equal([KentaFace.Url], scope.Downloader.Requested.Select(u => u.AbsoluteUri));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Sem_fonte_de_imagens_o_controle_mostra_o_marcador_e_nao_falha()
    {
        await fixture.Session.Dispatch<int>(
            () =>
            {
                VndbImages.Source = null;
                var cover = new Cover { Width = 72, Image = new VndbImageRef("https://t.vndb.org/cv/00/1.jpg"), Seed = "Jogo" };
                var window = new Window { Width = 200, Height = 200, Content = cover };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);
                    Assert.False(cover.HasImage);
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    // ----- Resultado do VNDB e ficha do VNDB -----

    private static FakeVndbCatalog Catalog()
    {
        VndbImage Cover(string path, double sexual = 0) => new($"https://t.vndb.org/cv/{path}.jpg", $"https://t.vndb.org/cv.t/{path}.jpg", 800, 1200, sexual, 0);
        var main = new VndbVisualNovel(
            "v31201", "Amaoto no Hakoniwa", "雨音の箱庭", [], ["Amaoto jogo"], "ja", 0, "2019-05-31", ["ja"], ["win"], Cover("00/1"), 3, 1080, 10,
            "Numa cidade onde chove quase o ano inteiro, Kenta volta à escola em que o irmão mais velho estudou.", [], 78.6);
        var disc = main with { Id = "v31202", Title = "Amaoto no Hakoniwa: Fan Disc", AltTitle = "雨音の箱庭 ファンディスク", Aliases = [], Released = "2020-03-01", Length = 2, LengthMinutes = 180, Image = Cover("00/2"), Rating = 72 };
        VndbCharacter Character(string id, string name, string original, string? gender, string role, double sexual = 0, params string[] aliases) =>
            new(id, name, original, aliases, null, new VndbImage($"https://t.vndb.org/ch/12/{id}.jpg", null, 256, 300, sexual, 0),
                gender is null ? null : new VndbGender(gender, gender), [new VndbCharacterAppearance("v31201", role, 0, null)], []);
        return new FakeVndbCatalog(
            [main, disc],
            [
                Character("c1", "Kenta Aizawa", "相沢 健太", "m", "main", 0, "Ken", "Kenta-kun"),
                Character("c2", "Aoi Sakura", "佐倉 葵", "f", "primary", 0, "Aoi-chan"),
                Character("c3", "Ren Hayase", "早瀬 蓮", "m", "side", 0, "Ren"),
                Character("c4", "Misaki Todo", "藤堂 美咲", "f", "side", 0, "Todo-sensei"),
                Character("c5", "Coadjuvante", "名無し", null, "appears"),
            ]);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_resultado_do_vndb_mostra_as_capas_e_a_ficha_os_retratos_a_coluna_No_jogo_e_o_personagem_sem_par(string themeName)
    {
        var theme = Variant(themeName);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var scope = Scope.Start(() => new LibraryPrivacy());
                var temp = scope.Temp;
                var game = temp.SubFolder("Amaoto jogo");
                File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
                var wizard = new NewProjectWizardViewModel(
                    new FakeFolderPicker(null),
                    [new YuRisFake()],
                    appSettings: new AppSettingsFile(temp.SubFolder("config")),
                    clock: new FixedClock(Now),
                    vndb: Catalog(),
                    gameCharacterNames: (_, _, _) => ["相沢 健太", "佐倉 葵", "早瀬 蓮", "藤堂 美咲", "名無し 08"]);
                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, newProjectWizard: () => wizard);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    start.NewProjectCommand.Execute(null);
                    await wizard.SetGameFolderAsync(game);
                    wizard.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
                    await wizard.NextCommand.ExecuteAsync(null);
                    Assert.Contains("Título lido do jogo (yscfg.ybn): 雨音の箱庭.", wizard.VndbDetail, StringComparison.Ordinal);
                    await WaitForImages(window, 2);

                    var covers = window.GetVisualDescendants().OfType<Cover>().Where(c => c.IsEffectivelyVisible).ToList();
                    Assert.Equal(2, covers.Count);
                    Assert.All(covers, c => Assert.Equal((72.0, 96.0), (c.Bounds.Width, c.Bounds.Height)));
                    Assert.Contains("2019 · Média · 18 h · YU-RIS · nota 7,9", VisibleTexts(window));
                    Assert.Contains("2020 · Curta · 3 h · YU-RIS · nota 7,2", VisibleTexts(window));
                    Snapshot(window, "vndb-imagens-resultado", theme);

                    await wizard.NextCommand.ExecuteAsync(null);
                    await WaitForImages(window, 4);
                    var portraits = window.GetVisualDescendants().OfType<Portrait>().Where(p => p.IsEffectivelyVisible).ToList();
                    Assert.Equal(5, portraits.Count);
                    Assert.All(portraits, p => Assert.Equal((28.0, 28.0), (p.Bounds.Width, p.Bounds.Height)));
                    Assert.Equal(4, portraits.Count(p => p.HasImage));
                    var texts = VisibleTexts(window);
                    Assert.Contains("5 do VNDB, 4 casados com os nomes do jogo (ES.CHAR.NAME)", texts);
                    Assert.Contains("O aviso “5 personagens sem gênero” cai para 1, o da personagem que o VNDB não tem.", texts);
                    Assert.Equal(["ES.CHAR.NAME 1", "ES.CHAR.NAME 2", "ES.CHAR.NAME 3", "ES.CHAR.NAME 4", "sem par no VNDB"], texts.Where(t => t.StartsWith("ES.CHAR.NAME", StringComparison.Ordinal) || t == "sem par no VNDB"));
                    Assert.DoesNotContain("Papel", texts);
                    Snapshot(window, "vndb-imagens-ficha", theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Uma engine que se diz YU-RIS e oferece o patch do executável, como a do teste do assistente.</summary>
    private sealed class YuRisFake : IEngine, IGamePatchProvider, IGameTitleSource
    {
        public string Id => "yuris";

        public GameTitleFile? ReadTitle(string unpackedDirectory) => null;

        public string? ReadTitleFromGame(string gameDir) => "雨音の箱庭";

        public byte[] WithTitle(GameTitleFile file, string title, ITextEncoder encoder) => [];

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("parece fake", new Dictionary<string, string>
        {
            ["ypfVersion"] = "500",
            ["scriptKey"] = "0xD36FAC96",
            ["scriptsArchive"] = "ysbin.ypf",
            ["scripts"] = "312",
        });

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public IReadOnlyList<IGamePatch> CreatePatches(string executable) => [];
    }

    private static string[] VisibleTexts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!)];

    // ----- Bíblia -----

    private static string BibleProject(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters(
        [
            new CharacterEntry("健太", "Kenta", CharacterGender.Male, VndbId: "c1", Image: new VndbImageRef("https://t.vndb.org/ch/12/c1.jpg")),
            new CharacterEntry("蓮", "Ren", CharacterGender.Male, VndbId: "c3", Image: new VndbImageRef("https://t.vndb.org/ch/12/c3.jpg")),
        ]);
        folder.SaveBible(GameBible.From(
        [
            new BibleScene("yst00001")
            {
                Name = "Atraso no portão",
                Summary = "Na chuva da manhã, Kenta e Ren chegam ao portão.",
                Characters = ["Kenta", "Ren", "Professor"],
            },
        ]) with { Synopsis = "Kenta se muda para uma cidade onde chove todo semestre." });
        return root;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_biblia_mostra_o_retrato_nos_chips_de_quem_aparece_e_o_marcador_para_quem_nao_tem_imagem(string themeName)
    {
        var theme = Variant(themeName);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var scope = Scope.Start(() => new LibraryPrivacy());
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(BibleProject(scope.Temp)), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Bible);
                    await vm.Bible.Loading;
                    await WaitForImages(window, 2);

                    var chips = window.GetVisualDescendants().OfType<Portrait>().Where(p => p.IsEffectivelyVisible).ToList();
                    Assert.Equal(3, chips.Count);
                    Assert.All(chips, p => Assert.Equal((28.0, 28.0), (p.Bounds.Width, p.Bounds.Height)));
                    Assert.Equal(["Kenta", "Ren", "Professor"], chips.Select(p => p.Seed));
                    Assert.Equal([true, true, false], chips.Select(p => p.HasImage));
                    Snapshot(window, "biblia-imagens", theme);
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
