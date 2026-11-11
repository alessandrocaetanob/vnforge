using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.SyntheticGames;

namespace VNForge.Core.Tests.Library;

/// <summary>A varredura da Biblioteca como a tela a faz: varre, junta, liga os projetos, casa com o VNDB e guarda a pasta com o que a pessoa escolheu.</summary>
public sealed class LibraryScanServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _games = new();
    private readonly TempFolder _config = new();

    public void Dispose()
    {
        _games.Dispose();
        _config.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryFile File => new(_config.Path);

    private static FakeVndbCatalog Catalog() => new(
    [
        Vn("v4", "CLANNAD", "クラナド", image: new VndbImage("https://t.vndb.org/cv/4.jpg", null, 300, 400, 0, 0)),
        Vn("v17", "Steins;Gate", "STEINS;GATE"),
    ]);

    private LibraryScanService Service(IVndbCatalog? catalog, params string[] projects) =>
        new(File, Scanner(), catalog is null ? null : () => new LibraryMatcher(catalog, new FixedTime()), () => projects, new FixedTime());

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Varre_junta_na_biblioteca_e_guarda_a_pasta_com_a_escolha_e_a_data()
    {
        Collection(_games.Path);
        var root = new LibraryRoot(_games.Path, IncludeSubfolders: true, MatchVndb: false, RescanOnOpen: true);

        var outcome = await Service(null).RunAsync(root, cancellationToken: Ct);

        Assert.Equal(6, outcome.Merge.Added.Count);
        Assert.Null(outcome.Match);
        var library = File.Load();
        Assert.Equal(5, library.Games.Count(g => g.Engine.Source != LibraryEngineSource.Unknown));
        Assert.Single(library.Games, g => g.Engine.Source == LibraryEngineSource.Unknown);
        var saved = Assert.Single(library.Roots);
        Assert.Equal(GameLibrary.NormalizeFolder(_games.Path), saved.Path);
        Assert.True(saved.RescanOnOpen);
        Assert.Equal(Now, saved.ScannedAt);
    }

    [Fact]
    public async Task Le_os_projetos_recentes_na_thread_de_quem_chama_e_nao_no_pool_da_varredura()
    {
        Collection(_games.Path);
        var caller = Environment.CurrentManagedThreadId;
        int? readOn = null;
        var service = new LibraryScanService(File, Scanner(), null, () =>
        {
            readOn = Environment.CurrentManagedThreadId;
            return [];
        }, new FixedTime());

        await service.RunAsync(new LibraryRoot(_games.Path, MatchVndb: false), cancellationToken: Ct);

        Assert.Equal(caller, readOn);
    }

    [Fact]
    public async Task Sem_subpastas_olha_so_a_pasta_apontada()
    {
        Collection(_games.Path);

        var outcome = await Service(null).RunAsync(new LibraryRoot(_games.Path, IncludeSubfolders: false, MatchVndb: false), cancellationToken: Ct);

        Assert.DoesNotContain(outcome.Scan.Games, g => g.Engine.Source != LibraryEngineSource.Unknown);
    }

    [Fact]
    public async Task Pasta_sem_engine_conhecida_entra_como_nao_reconhecida()
    {
        SyntheticGames.File(Path.Combine(_games.Path, "Toge no Kaji", "Toge.exe"));

        var outcome = await Service(null).RunAsync(new LibraryRoot(_games.Path, MatchVndb: false), cancellationToken: Ct);

        var game = Assert.Single(outcome.Scan.Games);
        Assert.Equal(LibraryEngineSource.Unknown, game.Engine.Source);
    }

    [Fact]
    public async Task Com_casamento_confirma_o_que_nao_deixa_duvida_guarda_os_candidatos_e_grava_jogo_a_jogo()
    {
        SyntheticGames.File(Path.Combine(_games.Path, "[Key] CLANNAD v1.02", "data.xp3"));
        SyntheticGames.File(Path.Combine(_games.Path, "[Key] CLANNAD v1.02", "CLANNAD.exe"));
        SyntheticGames.File(Path.Combine(_games.Path, "Desconhecido", "Scene.pck"));
        SyntheticGames.File(Path.Combine(_games.Path, "Desconhecido", "x.exe"));
        var matched = new List<LibraryMatchProgress>();

        var outcome = await Service(Catalog()).RunAsync(new LibraryRoot(_games.Path), matchProgress: new Collector<LibraryMatchProgress>(matched), cancellationToken: Ct);

        Assert.NotNull(outcome.Match);
        Assert.Equal(2, matched.Count);
        var library = File.Load();
        var clannad = library.Games.Single(g => g.Title == "CLANNAD");
        Assert.True(clannad.IsConfirmed);
        Assert.Equal("https://t.vndb.org/cv/4.jpg", clannad.Info!.CoverUrl);
        var unknown = library.Games.Single(g => GameLibrary.FolderName(g) == "Desconhecido");
        Assert.False(unknown.IsConfirmed);
        Assert.NotNull(unknown.SearchedAt);
    }

    [Fact]
    public async Task Varrer_de_novo_nao_desfaz_o_casamento_nem_busca_o_que_ja_foi_buscado()
    {
        SyntheticGames.File(Path.Combine(_games.Path, "[Key] CLANNAD v1.02", "data.xp3"));
        SyntheticGames.File(Path.Combine(_games.Path, "[Key] CLANNAD v1.02", "CLANNAD.exe"));
        var catalog = Catalog();
        var service = Service(catalog);
        await service.RunAsync(new LibraryRoot(_games.Path), cancellationToken: Ct);
        var calls = catalog.Calls.Count;

        var again = await service.RunAsync(new LibraryRoot(_games.Path), cancellationToken: Ct);

        Assert.Empty(again.Merge.Added);
        Assert.Equal(calls, catalog.Calls.Count);
        Assert.True(File.Load().Games.Single().IsConfirmed);
    }

    [Fact]
    public async Task Pausar_guarda_o_que_ja_foi_achado_e_nao_comeca_o_casamento()
    {
        Collection(_games.Path);
        using var cancel = new CancellationTokenSource();
        var progress = new Collector<LibraryScanProgress>([], report =>
        {
            if (report.Games >= 1)
            {
                cancel.Cancel();
            }
        });

        var outcome = await Service(Catalog()).RunAsync(new LibraryRoot(_games.Path), progress, cancellationToken: cancel.Token);

        Assert.True(outcome.Scan.Cancelled);
        Assert.Null(outcome.Match);
        Assert.NotEmpty(File.Load().Games);
        Assert.Single(File.Load().Roots);
    }

    [Fact]
    public async Task Liga_os_projetos_de_traducao_conhecidos_aos_jogos_da_pasta()
    {
        Collection(_games.Path);
        var gameFolder = Path.Combine(_games.Path, "Steins;Gate");
        var project = Path.Combine(_config.Path, "projetos", "sg");
        ProjectFolder.Create(project, new ProjectConfig("sg", new GameConfig(gameFolder, "SG.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("fold"), AiConfig.Default));

        var outcome = await Service(null, project).RunAsync(new LibraryRoot(_games.Path, MatchVndb: false), cancellationToken: Ct);

        var link = Assert.Single(outcome.Linked);
        Assert.Equal(project, link.ProjectPath);
        Assert.Equal(project, File.Load().Games.Single(g => g.Title == "Steins;Gate").ProjectPath);
    }

    [Fact]
    public async Task Progresso_traz_o_jogo_achado_e_a_estimativa_de_pastas()
    {
        Collection(_games.Path);
        var reports = new List<LibraryScanProgress>();

        await Service(null).RunAsync(new LibraryRoot(_games.Path, MatchVndb: false), new Collector<LibraryScanProgress>(reports), cancellationToken: Ct);

        Assert.Equal(6, reports.Count(r => r.Found is not null));
        Assert.All(reports, r => Assert.True(r.EstimatedDirectories >= r.Directories));
        Assert.True(reports[^1].EstimatedDirectories > 5);
    }

    [Fact]
    public async Task Pasta_que_nao_existe_e_erro_em_portugues()
    {
        var ex = await Assert.ThrowsAsync<LibraryException>(() => Service(null).RunAsync(new LibraryRoot(Path.Combine(_games.Path, "nada")), cancellationToken: Ct));

        Assert.Contains("A pasta não existe", ex.Message);
    }

    [Fact]
    public async Task Editor_confirma_procura_de_novo_dispensa_desfaz_e_tira_o_jogo()
    {
        SyntheticGames.File(Path.Combine(_games.Path, "[Key] CLANNAD v1.02", "data.xp3"));
        SyntheticGames.File(Path.Combine(_games.Path, "[Key] CLANNAD v1.02", "CLANNAD.exe"));
        await Service(null).RunAsync(new LibraryRoot(_games.Path, MatchVndb: false), cancellationToken: Ct);
        var editor = new LibraryEditor(File, () => new LibraryMatcher(Catalog(), new FixedTime()));
        var id = File.Load().Games.Single().Id;

        var searched = await editor.SearchAgainAsync(id, Ct);
        Assert.Equal(["v4"], searched.Candidates.Select(c => c.VndbId));
        Assert.False(searched.IsConfirmed);

        Assert.Empty(editor.DismissCandidates(id).Candidates);

        var confirmed = await editor.ConfirmAsync(id, "v4", Ct);
        Assert.Equal(LibraryConfirmation.Manual, confirmed.Confirmation);
        Assert.True(File.Load().Games.Single().IsConfirmed);

        var undone = editor.Unconfirm(id);
        Assert.False(undone.IsConfirmed);
        Assert.False(File.Load().Games.Single().IsConfirmed);

        await Assert.ThrowsAsync<LibraryException>(() => editor.ConfirmAsync(id, "v999", Ct));
        Assert.True(editor.Remove(id));
        Assert.False(editor.Remove(id));
        Assert.Empty(File.Load().Games);
    }

    private sealed class Collector<T>(List<T> items, Action<T>? also = null) : IProgress<T>
    {
        public void Report(T value)
        {
            items.Add(value);
            also?.Invoke(value);
        }
    }
}
