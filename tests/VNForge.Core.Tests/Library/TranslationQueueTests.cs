using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Library;

public sealed class TranslationQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly LibraryEngine Supported = new("yuris", "YU-RIS", LibraryEngineSource.Engine);
    private static readonly LibraryEngine Unsupported = new("kirikiri", "KiriKiri", LibraryEngineSource.Signature);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LibraryGame Game(string id, string title, string? vndbId, LibraryEngine? engine = null, int? minutes = null, int? category = null, string? project = null) =>
        new(id, "C:\\Jogos\\" + title, null, engine ?? Supported, title, null, T0)
        {
            VndbId = vndbId,
            Confirmation = vndbId is null ? LibraryConfirmation.None : LibraryConfirmation.Manual,
            ProjectPath = project,
            Info = vndbId is null ? null : new LibraryVndbInfo(title, null, null, null, null, category, minutes, 10, [], false),
        };

    private static VndbRelease Release(string id, string vn, string language = "pt-br", string type = "complete", bool machine = false) =>
        new(id, "Edição " + id, null, [new VndbReleaseLanguage(language, null, null, machine, false)], ["win"], "2020-01-01", null, true, true, false, null, null, [new VndbReleaseVisualNovel(vn, type)]);

    private static IEnumerable<VndbRelease> Releases(params (string Language, string Type, bool Machine)[] items) =>
        items.Select((item, index) => Release("r" + (index + 1), "v1", item.Language, item.Type, item.Machine));

    [Fact]
    public async Task Confere_so_o_que_a_fila_precisa_e_nao_repete_o_que_ja_sabe()
    {
        var library = new GameLibrary(
        [
            Game("g1", "Sem português", "v1"),
            Game("g2", "Com português", "v2"),
            Game("g3", "Só automática", "v3"),
            Game("g4", "Engine sem suporte", "v4", Unsupported),
            Game("g5", "Sem casamento", null),
            Game("g6", "Já em tradução", "v6", project: "C:\\Projetos\\seis"),
        ]);
        var fake = new FakeVndbCatalog(releases: [Release("r2", "v2"), Release("r3", "v3", machine: true)]);
        var checker = new PortugueseTranslationChecker(fake, new TimeProviderAt(T0));

        var run = await checker.CheckAllAsync(library, cancellationToken: Ct);

        Assert.Equal(["g1", "g2", "g3"], run.Checked.Select(g => g.Id));
        Assert.Equal(["GetReleasesAsync(v1)", "GetReleasesAsync(v2)", "GetReleasesAsync(v3)"], fake.Calls);
        Assert.Equal(PortugueseStatus.None, library.Find("g1").CurrentPortuguese?.Status);
        Assert.Equal(PortugueseStatus.Complete, library.Find("g2").CurrentPortuguese?.Status);
        Assert.Equal(PortugueseStatus.MachineOnly, library.Find("g3").CurrentPortuguese?.Status);
        Assert.Equal(T0, library.Find("g1").CurrentPortuguese?.CheckedAt);
        Assert.Null(library.Find("g4").Portuguese);

        var again = await checker.CheckAllAsync(library, cancellationToken: Ct);

        Assert.Empty(again.Checked);
        Assert.Equal(3, fake.Calls.Count);

        await checker.CheckAllAsync(library, again: true, cancellationToken: Ct);

        Assert.Equal(6, fake.Calls.Count);
    }

    [Fact]
    public async Task Avisa_a_cada_jogo_conferido_para_quem_grava_o_arquivo()
    {
        var library = new GameLibrary([Game("g1", "Um", "v1"), Game("g2", "Dois", "v2")]);
        var kept = new List<string>();

        await new PortugueseTranslationChecker(new FakeVndbCatalog()).CheckAllAsync(library, onChecked: game => kept.Add(game.Id), cancellationToken: Ct);

        Assert.Equal(["g1", "g2"], kept);
    }

    [Fact]
    public async Task Falha_do_vndb_vai_para_a_lista_de_falhas_e_tres_seguidas_param_o_lote()
    {
        var library = new GameLibrary([Game("g1", "Um", "v1"), Game("g2", "Dois", "v2"), Game("g3", "Três", "v3"), Game("g4", "Quatro", "v4")]);

        var run = await new PortugueseTranslationChecker(new ReleasesDown()).CheckAllAsync(library, cancellationToken: Ct);

        Assert.Equal(3, run.Failures.Count);
        Assert.Contains("fora do ar", run.AbortReason, StringComparison.Ordinal);
        Assert.Empty(run.Checked);
        Assert.Null(library.Find("g4").Portuguese);
    }

    [Fact]
    public async Task Lista_de_edicoes_cortada_sem_traducao_achada_fica_sem_resposta()
    {
        var library = new GameLibrary([Game("g1", "Um", "v1")]);

        var run = await new PortugueseTranslationChecker(new ReleasesTruncated()).CheckAllAsync(library, cancellationToken: Ct);

        Assert.Equal(["g1"], run.Incomplete.Select(g => g.Id));
        Assert.Empty(run.Checked);
        Assert.Null(library.Find("g1").Portuguese);
    }

    [Fact]
    public async Task A_fila_ordena_o_que_falta_traduzir_primeiro_e_o_mais_curto_na_frente()
    {
        var library = new GameLibrary(
        [
            Game("longo", "Longo", "v1", minutes: 3000),
            Game("curto", "Curto", "v2", minutes: 120),
            Game("categoria", "Categoria", "v3", category: 2),
            Game("sem-tempo", "Sem tempo", "v4"),
            Game("automatica", "Automática", "v5", minutes: 60),
            Game("parcial", "Parcial", "v6", minutes: 60),
            Game("sem-casamento", "Sem casamento", null),
            Game("pronto", "Pronto", "v8", minutes: 60),
            Game("em-andamento", "Em andamento", "v9", project: "C:\\Projetos\\nove"),
            Game("outra-engine", "Outra engine", "v10", Unsupported, minutes: 60),
        ]);
        var fake = new FakeVndbCatalog(releases:
        [
            Release("r5", "v5", machine: true),
            Release("r6", "v6", type: "partial"),
            Release("r8", "v8"),
        ]);
        await new PortugueseTranslationChecker(fake).CheckAllAsync(library, cancellationToken: Ct);

        var entries = TranslationQueue.Build(library.Games);

        Assert.Equal(
            ["curto", "categoria", "longo", "sem-tempo", "automatica", "parcial", "sem-casamento", "em-andamento", "pronto", "outra-engine"],
            entries.Select(e => e.Game.Id));
        Assert.Equal(
            [TranslationQueueState.Queued, TranslationQueueState.Queued, TranslationQueueState.Queued, TranslationQueueState.Queued, TranslationQueueState.Queued, TranslationQueueState.Queued, TranslationQueueState.Queued, TranslationQueueState.InProgress, TranslationQueueState.AlreadyTranslated, TranslationQueueState.UnsupportedEngine],
            entries.Select(e => e.State));
        Assert.Equal(["curto", "categoria", "longo", "sem-tempo", "automatica", "parcial", "sem-casamento"], TranslationQueue.Pending(library.Games).Select(e => e.Game.Id));
    }

    private sealed class TimeProviderAt(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private abstract class UnusedCatalog : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => throw new NotSupportedException();

        public abstract Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default);
    }

    private sealed class ReleasesDown : UnusedCatalog
    {
        public override Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) =>
            throw new VndbException("O VNDB está fora do ar.");
    }

    private sealed class ReleasesTruncated : UnusedCatalog
    {
        public override Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) =>
            Task.FromResult(new VndbResults<VndbRelease>([Release("r1", visualNovelId, "en")], Truncated: true));
    }
}
