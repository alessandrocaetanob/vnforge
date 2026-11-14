using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A tela da Biblioteca (#548) sem janela: carga, filtros, gaveta, casamento, varredura e privacidade, sobre pastas e <c>library.json</c> reais e um VNDB de mentira.</summary>
[Trait("Risco", "R1")]
[Trait("Risco", "R7")]
public sealed class LibraryViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();
    private readonly TempFolder _games = new();

    public void Dispose()
    {
        _config.Dispose();
        _games.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FixedClock Clock => new(LibrarySamples.Now);

    private LibraryFile File => new(_config.Path);

    private static VndbVisualNovel Vn(string id, string title, string? alt = null) =>
        new(id, title, alt, [], [], "ja", 0, "2008-04-24", ["ja"], ["win"], new VndbImage("https://t.vndb.org/cv/" + id + ".jpg", null, 300, 400, 0, 0), 4, 3000, 10, null, []);

    private static FakeVndbCatalog Catalog() => new([Vn("v4", "CLANNAD", "クラナド"), Vn("v7", "Kōrin no Mori", "光輪の森")]);

    private LibraryViewModel Create(
        IFolderPicker? picker = null,
        bool withServices = true,
        Action<string>? openFolder = null,
        FakeVndbCatalog? catalog = null,
        Action<Action>? post = null)
    {
        LibraryScanService? scan = null;
        LibraryEditor? editor = null;
        if (withServices)
        {
            var vndb = catalog ?? Catalog();
            scan = new LibraryScanService(File, new LibraryScanner(new EngineRecognizer([])), () => new LibraryMatcher(vndb, Clock), () => [], Clock);
            editor = new LibraryEditor(File, () => new LibraryMatcher(vndb, Clock));
        }

        return new LibraryViewModel(File, picker ?? new FakeFolderPicker(null), post ?? (action => action()), new LibraryServices { ScanService = scan, Editor = editor, Clock = Clock, ReadTranslation = LibrarySamples.Translation, Sheets = new LibrarySamples.Sheets(), OpenFolder = openFolder });
    }

    private async Task<LibraryViewModel> Loaded(Action<LibraryViewModel>? configure = null, bool withServices = true, Action<string>? openFolder = null)
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        var vm = Create(withServices: withServices, openFolder: openFolder);
        configure?.Invoke(vm);
        await vm.LoadAsync();
        return vm;
    }

    private static LibraryCardViewModel Card(LibraryViewModel vm, string title) => vm.Cards.Single(c => c.Title == title);

    [Fact]
    public async Task Arquivo_ilegivel_vira_aviso_e_nao_lista_vazia_muda()
    {
        Directory.CreateDirectory(_config.Path);
        await System.IO.File.WriteAllTextAsync(File.Path, "{ isto não é json", Ct);
        var vm = Create();

        await vm.LoadAsync();

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Equal(LibraryMode.Empty, vm.Mode);
    }

    [Fact]
    public async Task Desfazer_o_casamento_e_tirar_da_biblioteca_mexem_so_no_library_json()
    {
        var vm = await Loaded();
        var amaoto = Card(vm, "Amaoto no Hakoniwa");

        await vm.UnconfirmCommand.ExecuteAsync(amaoto);

        Assert.False(File.Load().Games.Single(g => GameLibrary.FolderName(g) == "amaoto_hakoniwa").IsConfirmed);

        var fuyu = Card(vm, "Fuyu no Shiori");
        vm.Select(fuyu);
        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(11, vm.Cards.Count);
        Assert.DoesNotContain(vm.Cards, c => c.Title == "Fuyu no Shiori");
        Assert.Null(vm.Selected);
        Assert.Equal(11, File.Load().Games.Count);
    }

    private void Collection()
    {
        Touch("[Key] CLANNAD v1.02", "data.xp3");
        Touch("[Key] CLANNAD v1.02", "CLANNAD.exe");
        Touch("Steins;Gate", "Scene.pck");
        Touch("Steins;Gate", "Steins.exe");
        Touch("Sem Nome", "x.exe");
    }

    private void Touch(string folder, string file)
    {
        var path = Path.Combine(_games.Path, folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "x");
    }

    [Fact]
    public async Task Varrer_de_novo_lista_todas_as_pastas_sem_acesso_no_fim_mesmo_com_pasta_boa_no_meio()
    {
        Collection();
        var missing1 = Path.Combine(_games.Path, "Sumiu 1");
        var missing2 = Path.Combine(_games.Path, "Sumiu 2");
        File.Update(l =>
        {
            l.SetRoot(new LibraryRoot(missing1));
            l.SetRoot(new LibraryRoot(_games.Path));
            l.SetRoot(new LibraryRoot(missing2));
        });
        var vm = Create();
        await vm.LoadAsync();

        await vm.RescanCommand.ExecuteAsync(null);

        Assert.Contains(GameLibrary.NormalizeFolder(missing1), vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(GameLibrary.NormalizeFolder(missing2), vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(3, File.Load().Games.Count);
    }
}
