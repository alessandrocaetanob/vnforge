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
    public async Task Sem_subpastas_olha_so_a_pasta_apontada()
    {
        Collection(_games.Path);

        var outcome = await Service(null).RunAsync(new LibraryRoot(_games.Path, IncludeSubfolders: false, MatchVndb: false), cancellationToken: Ct);

        Assert.DoesNotContain(outcome.Scan.Games, g => g.Engine.Source != LibraryEngineSource.Unknown);
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
