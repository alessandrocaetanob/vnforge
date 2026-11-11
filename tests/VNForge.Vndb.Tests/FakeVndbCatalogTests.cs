using VNForge.Core.Vndb;

namespace VNForge.Vndb.Tests;

public class FakeVndbCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VndbVisualNovel Vn(string id, string title, string? alt = null, params string[] aliases) =>
        new(id, title, alt, [], aliases, "ja", 0, null, [], [], null, null, null, 0, null, []);

    private static VndbCharacter Character(string id, string name, params string[] vns) =>
        new(id, name, null, [], null, null, null, [.. vns.Select(v => new VndbCharacterAppearance(v, "side", 0, null))], []);

    private static FakeVndbCatalog Catalog() => new(
        [Vn("v1", "Moon Flower", "月の花", "Tsukihana"), Vn("v2", "Sun Garden"), Vn("v3", "Moon Garden")],
        [Character("c1", "Ami", "v1", "v2"), Character("c2", "Ren", "v2"), Character("c3", "Sora", "v3")],
        [new VndbRelease("r1", "Moon Flower", null, [], [], null, null, false, false, true, null, null, [new VndbReleaseVisualNovel("v1", "complete")])]);

    [Fact]
    public async Task Busca_por_titulo_olha_titulo_alternativo_e_apelido_sem_diferenciar_maiusculas()
    {
        var catalog = Catalog();

        Assert.Equal(["v1", "v3"], (await catalog.SearchVisualNovelsAsync("MOON", ct: Ct)).Select(v => v.Id));
        Assert.Equal(["v1"], (await catalog.SearchVisualNovelsAsync("月の", ct: Ct)).Select(v => v.Id));
        Assert.Equal(["v1"], (await catalog.SearchVisualNovelsAsync("tsukihana", ct: Ct)).Select(v => v.Id));
        Assert.Empty(await catalog.SearchVisualNovelsAsync("inexistente", ct: Ct));
    }

    [Fact]
    public async Task Edicoes_e_jogo_por_id()
    {
        var catalog = Catalog();

        Assert.Equal(["r1"], (await catalog.GetReleasesAsync("v1", ct: Ct)).Items.Select(r => r.Id));
        Assert.Empty((await catalog.GetReleasesAsync("v2", ct: Ct)).Items);
        Assert.Equal("Sun Garden", (await catalog.GetVisualNovelAsync("v2", ct: Ct))?.Title);
        Assert.Null(await catalog.GetVisualNovelAsync("v99", ct: Ct));
    }

    [Fact]
    public async Task Registra_as_chamadas_na_ordem_sem_expor_o_token()
    {
        var catalog = Catalog();

        await catalog.SearchVisualNovelsAsync("moon", ct: Ct);
        await catalog.GetAuthInfoAsync(FakeVndbCatalog.ValidToken, ct: Ct);

        Assert.Equal(["SearchVisualNovelsAsync(moon)", "GetAuthInfoAsync(***)"], catalog.Calls);
    }

    [Fact]
    public async Task Atualizar_a_lista_aplica_gravar_apagar_e_manter()
    {
        var catalog = Catalog();
        var token = FakeVndbCatalog.ValidToken;

        await catalog.UpdateEntryAsync(token, "v1", new VndbListUpdate { Vote = 70, Notes = "bom", Started = new DateOnly(2025, 1, 2), LabelsSet = [1, 2] }, ct: Ct);
        await catalog.UpdateEntryAsync(token, "v1", new VndbListUpdate { Notes = VndbChange<string>.Clear, LabelsUnset = [1] }, ct: Ct);

        var entry = Assert.Single((await catalog.GetListAsync("u1", token, ct: Ct)).Items);
        Assert.Equal("v1", entry.VisualNovelId);
        Assert.Equal("Moon Flower", entry.Title);
        Assert.Equal(70, entry.Vote);
        Assert.Null(entry.Notes);
        Assert.Equal(new DateOnly(2025, 1, 2), entry.Started);
        Assert.Equal([2, 7], entry.Labels.Select(l => l.Id));
    }

    [Fact]
    public async Task Token_errado_vira_erro_401_como_no_servico_real()
    {
        var catalog = Catalog();

        var error = await Assert.ThrowsAsync<VndbException>(() => catalog.UpdateEntryAsync("outro", "v1", new VndbListUpdate(), ct: Ct));

        Assert.Equal(401, error.StatusCode);
        await Assert.ThrowsAsync<VndbException>(() => catalog.GetAuthInfoAsync("outro", ct: Ct));
    }

    [Fact]
    public async Task Lista_de_outra_conta_vem_vazia()
    {
        var catalog = Catalog();
        await catalog.UpdateEntryAsync(FakeVndbCatalog.ValidToken, "v1", new VndbListUpdate { Vote = 50 }, ct: Ct);

        Assert.Empty((await catalog.GetListAsync("u2", ct: Ct)).Items);
        Assert.Single((await catalog.GetListAsync("u1", ct: Ct)).Items);
    }

    [Fact]
    public async Task Votar_poe_o_rotulo_Voted_e_a_data_e_apagar_o_voto_tira_os_dois()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var catalog = new FakeVndbCatalog([Vn("v1", "Moon Flower")], time: time);
        var token = FakeVndbCatalog.ValidToken;

        await catalog.UpdateEntryAsync(token, "v1", new VndbListUpdate { Vote = 80 }, ct: Ct);
        var voted = Assert.Single((await catalog.GetListAsync("u1", ct: Ct)).Items);
        Assert.Equal(time.GetUtcNow(), voted.Voted);
        Assert.Equal([7], voted.Labels.Select(l => l.Id));
        Assert.Equal("Voted", voted.Labels[0].Label);

        await catalog.UpdateEntryAsync(token, "v1", new VndbListUpdate { Vote = VndbChange<int>.Clear }, ct: Ct);
        var cleared = Assert.Single((await catalog.GetListAsync("u1", ct: Ct)).Items);
        Assert.Null(cleared.Vote);
        Assert.Null(cleared.Voted);
        Assert.Empty(cleared.Labels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(1601)]
    public async Task Rotulos_invalidos_sao_recusados_ao_gravar_e_ao_tirar(int label)
    {
        var catalog = Catalog();
        var token = FakeVndbCatalog.ValidToken;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => catalog.UpdateEntryAsync(token, "v1", new VndbListUpdate { LabelsSet = [label] }, ct: Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => catalog.UpdateEntryAsync(token, "v1", new VndbListUpdate { LabelsUnset = [label] }, ct: Ct));
        Assert.Empty((await catalog.GetListAsync("u1", ct: Ct)).Items);
    }

    [Fact]
    public async Task Resultados_de_catalogo_nunca_vem_cortados()
    {
        var catalog = Catalog();

        Assert.False((await catalog.GetCharactersAsync("v1", ct: Ct)).Truncated);
        Assert.False((await catalog.SearchCharactersByTitleAsync("moon", ct: Ct)).Truncated);
        Assert.False((await catalog.GetReleasesAsync("v1", ct: Ct)).Truncated);
    }
}
