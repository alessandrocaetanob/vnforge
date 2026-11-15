using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.SyntheticGames;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R6")]
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
