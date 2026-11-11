using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.SyntheticGames;

namespace VNForge.Core.Tests.Library;

public class LibraryMatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly LibraryMatchOptions Auto = new() { AutoConfirm = true };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LibraryGame Game(string folderName, string? exe = null, string? window = null) =>
        new(
            GameLibrary.IdFor(Path.Combine("colecao", folderName)),
            GameLibrary.NormalizeFolder(Path.Combine("colecao", folderName)),
            exe,
            new LibraryEngine("kirikiri", "KiriKiri", LibraryEngineSource.Signature),
            folderName,
            window,
            Now);

    private static FakeVndbCatalog Catalog() => new(
    [
        Vn("v4", "CLANNAD", "クラナド", description: "Sinopse [spoiler]final[/spoiler]."),
        Vn("v17", "Steins;Gate", "STEINS;GATE", ["Shtainzu Geito"]),
        Vn("v21", "Fate/stay night", "Fate/stay night"),
        Vn("v22", "Fate/stay night Realta Nua", null),
    ]);

    private static LibraryMatcher Matcher(IVndbCatalog catalog) => new(catalog, new FakeTime());

    private sealed class FakeTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Abaixo_do_limiar_nunca_confirma_sozinho_mesmo_pedindo()
    {
        var matched = await Matcher(Catalog()).MatchAsync(Game("Clannad Full Voice", "Clannad.exe"), Auto, Ct);

        Assert.False(matched.IsConfirmed);
        var candidate = Assert.Single(matched.Candidates);
        Assert.Equal("v4", candidate.VndbId);
        Assert.InRange(candidate.Score, 0.5, 0.89);
    }

    [Fact]
    public async Task Empate_entre_dois_jogos_nao_confirma()
    {
        var catalog = new FakeVndbCatalog([Vn("v1", "Sakura"), Vn("v2", "Sakura")]);

        var matched = await Matcher(catalog).MatchAsync(Game("Sakura"), Auto, Ct);

        Assert.False(matched.IsConfirmed);
        Assert.Equal(["v1", "v2"], matched.Candidates.Select(c => c.VndbId));
        Assert.Equal(matched.Candidates[0].Score, matched.Candidates[1].Score);
    }

    [Fact]
    public async Task Para_de_buscar_quando_o_primeiro_termo_ja_decide_e_limita_as_buscas_quando_nao()
    {
        var decided = Catalog();
        await Matcher(decided).MatchAsync(Game("Steins;Gate", "SG.exe", "Steins;Gate"), Auto, Ct);
        var undecided = Catalog();
        await Matcher(undecided).MatchAsync(Game("Nada Aqui", "Nenhum.exe", "Janela Estranha"), Auto, Ct);

        Assert.Single(decided.Calls);
        Assert.Equal(["SearchVisualNovelsAsync(Janela Estranha)", "SearchVisualNovelsAsync(Nada Aqui)", "SearchVisualNovelsAsync(Nenhum)"], undecided.Calls);
    }

    [Fact]
    public async Task Jogo_ja_confirmado_volta_como_esta_sem_chamar_o_vndb()
    {
        var catalog = Catalog();
        var confirmed = LibraryMatcher.Confirm(Game("CLANNAD"), Vn("v4", "CLANNAD"), LibraryConfirmation.Manual);

        var again = await Matcher(catalog).MatchAsync(confirmed, Auto, Ct);

        Assert.Same(confirmed, again);
        Assert.Empty(catalog.Calls);
    }

    [Fact]
    public async Task Lote_casa_so_quem_nao_foi_buscado_e_vai_trocando_na_biblioteca()
    {
        var library = new GameLibrary(
        [
            Game("CLANNAD"),
            Game("Steins;Gate"),
            Game("Jogo Inexistente"),
            Game("Fate"),
            LibraryMatcher.Confirm(Game("Ja Confirmado"), Vn("v99", "Ja Confirmado"), LibraryConfirmation.Manual),
            Game("Ja Buscado") with { SearchedAt = Now },
        ]);
        var catalog = Catalog();
        var progress = new List<LibraryMatchProgress>();

        var run = await Matcher(catalog).MatchAllAsync(library, Auto, progress: new Sync<LibraryMatchProgress>(progress.Add), cancellationToken: Ct);

        Assert.Equal(["CLANNAD", "Steins;Gate"], run.Confirmed.Select(g => g.Title));
        Assert.Equal(["Jogo Inexistente"], run.NotFound.Select(g => GameLibrary.FolderName(g)));
        Assert.Equal(["Fate"], run.Pending.Select(g => GameLibrary.FolderName(g)));
        Assert.Empty(run.Failures);
        Assert.Equal(4, progress.Count);
        Assert.Equal("v4", library.Find("CLANNAD").VndbId);
        Assert.DoesNotContain(catalog.Calls, c => c.Contains("Ja Buscado", StringComparison.Ordinal) || c.Contains("Ja Confirmado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lote_com_again_busca_de_novo_quem_ja_foi_buscado_e_com_only_busca_um_so()
    {
        var searched = Game("Jogo Inexistente") with { SearchedAt = Now.AddDays(-1) };
        var other = Game("CLANNAD");
        var library = new GameLibrary([searched, other]);

        var onlyOne = await Matcher(Catalog()).MatchAllAsync(library, Auto, onlyGame: other.Id, cancellationToken: Ct);
        var again = await Matcher(Catalog()).MatchAllAsync(library, Auto, again: true, cancellationToken: Ct);

        Assert.Equal(["CLANNAD"], onlyOne.Confirmed.Select(g => g.Title));
        Assert.Equal(["Jogo Inexistente"], again.NotFound.Select(g => GameLibrary.FolderName(g)));
        Assert.Equal(Now, library.Find("Jogo Inexistente").SearchedAt);
    }

    [Fact]
    public async Task Para_o_lote_depois_de_tres_falhas_seguidas()
    {
        var library = new GameLibrary(Enumerable.Range(1, 6).Select(i => Game($"Jogo {i}")));
        var catalog = new FailingCatalog(Catalog(), "jogo");

        var run = await Matcher(catalog).MatchAllAsync(library, Auto, cancellationToken: Ct);

        Assert.Equal(3, run.Failures.Count);
        Assert.Contains("3 vezes seguidas", run.AbortReason);
        Assert.All(library.Games, g => Assert.Null(g.SearchedAt));
    }

    [Fact]
    public async Task Cancelar_para_o_lote_e_guarda_o_que_ja_foi_feito()
    {
        var library = new GameLibrary([Game("CLANNAD"), Game("Steins;Gate")]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new Sync<LibraryMatchProgress>(p =>
        {
            if (p.Done == 1)
            {
                source.Cancel();
            }
        });

        var run = await Matcher(Catalog()).MatchAllAsync(library, Auto, progress: progress, cancellationToken: source.Token);

        Assert.True(run.Cancelled);
        Assert.Equal(["CLANNAD"], run.Confirmed.Select(g => g.Title));
        Assert.Null(library.Find("Steins;Gate").SearchedAt);
    }

    [Fact]
    public async Task Confirmar_a_mao_busca_a_ficha_e_recusa_id_que_o_vndb_nao_tem()
    {
        var matcher = Matcher(Catalog());
        var game = Game("qualquer coisa");

        var confirmed = await matcher.ConfirmAsync(game, "v17", Ct);
        var e = await Assert.ThrowsAsync<LibraryException>(() => matcher.ConfirmAsync(game, "v999", Ct));

        Assert.Equal("v17", confirmed.VndbId);
        Assert.Equal(LibraryConfirmation.Manual, confirmed.Confirmation);
        Assert.Equal("Steins;Gate", confirmed.Title);
        Assert.Null(confirmed.MatchScore);
        Assert.Contains("v999", e.Message);
    }

    [Fact]
    public async Task Confirmar_a_mao_um_candidato_guarda_a_pontuacao_dele()
    {
        var matcher = Matcher(Catalog());
        var matched = await matcher.MatchAsync(Game("CLANNAD"), cancellationToken: Ct);

        var confirmed = await matcher.ConfirmAsync(matched, "v4", Ct);

        Assert.Equal(matched.Candidates[0].Score, confirmed.MatchScore);
    }

    private sealed class Sync<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    /// <summary>Faz o catálogo falhar para os títulos que contêm <paramref name="failFor"/>.</summary>
    private sealed class FailingCatalog(FakeVndbCatalog inner, string failFor) : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) =>
            title.Contains(failFor, StringComparison.OrdinalIgnoreCase)
                ? throw new VndbException("rede fora")
                : inner.SearchVisualNovelsAsync(title, limit, ct);

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => inner.GetVisualNovelAsync(id, ct);

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => inner.GetCharactersAsync(visualNovelId, ct);

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => inner.SearchCharactersByTitleAsync(title, ct);

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => inner.GetReleasesAsync(visualNovelId, ct);
    }
}
