using System.Text.Json;
using VNForge.AI.Sheet;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

/// <summary>#558: <c>vnf library characters</c> com o catálogo falso do VNDB e o provedor falso de IA da ficha.</summary>
public sealed class LibraryCharactersCommandTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VndbVisualNovel Game(string id, string title) =>
        new(id, title, null, [], [], "ja", 2, "2004-04-28", ["ja"], ["win"], null, 4, 2700, 80, "Texto.", [new VndbTag("g10", "Comedy", "cont", 2.5, 0, false)]);

    private static VndbCharacter Tomoya() =>
        new("c1", "Tomoya Okazaki", "岡崎 朋也", ["Tomo"], "The hero. [spoiler]He is a ghost.[/spoiler]", null, new VndbGender("m", "f"),
            [new VndbCharacterAppearance("v4", "main", 0, null), new VndbCharacterAppearance("v5", "side", 1, null), new VndbCharacterAppearance("v6", "primary", 0, null)],
            [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false), new VndbTrait("i2", "Amnesia", "i20", "Personality", 1, false, false)]);

    private static VndbCharacter Sister() =>
        new("c2", "Ghost Sister", null, [], "Who is she?", null, null, [new VndbCharacterAppearance("v4", "side", 2, null)], []);

    private static VndbCharacter Kyou() =>
        new("c3", "Kyou Fujibayashi", null, [], "A classmate.", null, new VndbGender("f", "f"),
            [new VndbCharacterAppearance("v5", "main", 0, "r10"), new VndbCharacterAppearance("v5", "side", 2, "r11")],
            [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false)]);

    private static VndbCharacter Outsider() =>
        new("c9", "Only Elsewhere", null, [], "Not here.", null, null, [new VndbCharacterAppearance("v6", "main", 0, null)], []);

    private static VndbRelease Release(string id, string title) =>
        new(id, title, null, [], ["win"], "2008-01-01", null, false, false, true, null, null, [new VndbReleaseVisualNovel("v5", "complete")]);

    private FakeVndbCatalog Prepare(string secondFolder = "Little Busters", bool confirmed = true)
    {
        LibraryGame Owned(string id, string folder, string title, string? vndb) =>
            new(id, $"C:\\Jogos\\{folder}", null, LibraryEngine.Unknown, title, null, T0) { VndbId = vndb, Confirmation = vndb is null ? LibraryConfirmation.None : LibraryConfirmation.Manual };

        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([Owned("g1", "CLANNAD", "CLANNAD", confirmed ? "v4" : null), Owned("g2", secondFolder, "Little Busters", confirmed ? "v5" : null)]));
        var catalog = new FakeVndbCatalog(
            [Game("v4", "CLANNAD"), Game("v5", "Little Busters"), Game("v6", "Outside")],
            [Tomoya(), Sister(), Kyou(), Outsider()],
            [Release("r10", "Little Busters"), Release("r11", "Little Busters Perfect Edition")]);
        _cli.Cli.VndbCatalogFactory = () => catalog;
        _cli.Cli.SheetTranslatorFactory = (_, _) => new FakeSheetTranslator(item => item.Source == "Long Hair" ? "Cabelo longo" : FakeSheetTranslator.Prefix + item.Source);
        return catalog;
    }

    private sealed class TruncatedReleases(IVndbCatalog inner) : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) => inner.SearchVisualNovelsAsync(title, limit, ct);

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => inner.GetVisualNovelAsync(id, ct);

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => inner.GetCharactersAsync(visualNovelId, ct);

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => inner.SearchCharactersByTitleAsync(title, ct);

        public async Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) =>
            (await inner.GetReleasesAsync(visualNovelId, ct)) with { Truncated = true };
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private async Task Ready(bool translate = false)
    {
        await Run("library", "characters", "sync");
        if (translate)
        {
            await Run("library", "sheet", "translate", "CLANNAD", "--yes");
            await Run("library", "sheet", "translate", "g2", "--yes");
        }
    }

    [Fact]
    public async Task Sync_traz_os_personagens_dos_jogos_da_biblioteca_e_casa_as_edicoes()
    {
        var catalog = Prepare();

        var result = await Run("library", "characters", "sync");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2 jogos do VNDB: 2 buscados, 0 já estavam guardados; 4 personagens trazidos e 1 edição casada", result.Output, StringComparison.Ordinal);
        Assert.Contains("vndb.org", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(catalog.Calls, c => c.Contains("v6", StringComparison.Ordinal));
        Assert.Equal(["c1", "c2", "c3"], new VNForge.Core.Factsheet.SheetStore(_cli.UserConfigDirectory).LoadAllCharacters().Select(c => c.Id));
        Assert.Equal(["g1", "g2"], new GameEditionsFile(_cli.UserConfigDirectory).Load().GameIds);
    }

    [Fact]
    public async Task Sync_de_novo_usa_o_que_esta_guardado_e_nao_chama_o_VNDB()
    {
        var catalog = Prepare();
        await Run("library", "characters", "sync");
        var before = catalog.Calls.Count;

        var again = await Run("library", "characters", "sync", "--json");
        var callsAfterSecond = catalog.Calls.Count;
        var forced = await Run("library", "characters", "sync", "--again");

        Assert.Equal(0, again.ExitCode);
        Assert.Equal((2, 0, 2), (Data(again).GetProperty("games").GetInt32(), Data(again).GetProperty("fetched").GetInt32(), Data(again).GetProperty("cached").GetInt32()));
        Assert.Equal(before, callsAfterSecond);
        Assert.Contains("2 buscados", forced.Output, StringComparison.Ordinal);
        Assert.True(catalog.Calls.Count > callsAfterSecond);
    }

    [Fact]
    public async Task Sync_sem_jogo_confirmado_manda_casar_antes()
    {
        Prepare(confirmed: false);

        var result = await Run("library", "characters", "sync");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf library match", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_com_jogo_que_o_VNDB_nao_tem_avisa_e_sai_com_erro()
    {
        Prepare();
        _cli.Cli.VndbCatalogFactory = () => new FakeVndbCatalog([Game("v4", "CLANNAD")], [Tomoya()]);

        var result = await Run("library", "characters", "sync");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Ficou de fora (v5): O VNDB não tem o jogo v5.", result.Output, StringComparison.Ordinal);
        Assert.Contains("1 buscados", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_mostra_cada_personagem_uma_vez_com_os_jogos_da_biblioteca_e_esconde_o_que_e_spoiler()
    {
        Prepare();
        await Ready();

        var result = await Run("library", "characters", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 personagem na biblioteca:", result.Output, StringComparison.Ordinal);
        Assert.Contains("c1  Tomoya Okazaki (岡崎 朋也) · CLANNAD (protagonista)", result.Output, StringComparison.Ordinal);
        Assert.Contains("(mais 1 jogo escondido)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Little Busters (secundário)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Kyou", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Outside", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Only Elsewhere", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghost Sister", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 personagens escondidos por spoiler", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_com_show_spoilers_mostra_o_jogo_escondido_e_o_personagem_de_spoiler()
    {
        Prepare();
        await Ready();

        var result = await Run("library", "characters", "list", "--show-spoilers");

        Assert.Contains("3 personagens na biblioteca:", result.Output, StringComparison.Ordinal);
        Assert.Contains("CLANNAD (protagonista), Little Busters (secundário)", result.Output, StringComparison.Ordinal);
        Assert.Contains("Ghost Sister · CLANNAD (secundário)", result.Output, StringComparison.Ordinal);
        Assert.Contains("c3  Kyou Fujibayashi · Little Busters (protagonista)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_busca_por_nome_apelido_e_jogo_e_filtra_por_traco_em_portugues()
    {
        Prepare();
        await Ready(translate: true);

        var byName = await Run("library", "characters", "list", "tomo");
        var byGame = await Run("library", "characters", "list", "--appears-in", "Little", "--show-spoilers");
        var byTrait = await Run("library", "characters", "list", "--trait", "cabelo longo");
        var byId = await Run("library", "characters", "list", "--trait", "i1", "--trait", "i2");
        var anyOf = await Run("library", "characters", "list", "--trait", "i1", "--trait", "i2", "--any", "--show-spoilers");
        var none = await Run("library", "characters", "list", "zzz");

        Assert.Contains("1 personagem na busca:", byName.Output, StringComparison.Ordinal);
        Assert.Contains("Tomoya Okazaki", byName.Output, StringComparison.Ordinal);
        Assert.Contains("Kyou Fujibayashi", byGame.Output, StringComparison.Ordinal);
        Assert.Contains("1 personagem na busca:", byTrait.Output, StringComparison.Ordinal);
        Assert.Contains("Nenhum personagem da biblioteca combina", byId.Output, StringComparison.Ordinal);
        Assert.Contains("2 personagens na busca:", anyOf.Output, StringComparison.Ordinal);
        Assert.Contains("Nenhum personagem da biblioteca combina", none.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Busca_de_texto_por_personagem_escondido_nao_revela_que_ele_existe()
    {
        Prepare();
        await Ready();

        var result = await Run("library", "characters", "list", "Ghost");
        var json = await Run("library", "characters", "list", "Ghost", "--json");

        Assert.Contains("Nenhum personagem da biblioteca combina com a busca.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("escondido", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, Data(json).GetProperty("hidden").GetInt32());
    }

    [Fact]
    public async Task Sync_com_lista_de_edicoes_cortada_avisa_sem_falhar_e_guarda_o_casamento()
    {
        var catalog = Prepare();
        _cli.Cli.VndbCatalogFactory = () => new TruncatedReleases(catalog);

        var result = await Run("library", "characters", "sync");
        var json = await Run("library", "characters", "sync", "--again", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso: o VNDB tem mais edições do que o limite trazido em 2 pastas", result.Output, StringComparison.Ordinal);
        Assert.Equal(2, Data(json).GetProperty("editionsIncomplete").GetInt32());
        Assert.True(new GameEditionsFile(_cli.UserConfigDirectory).Load().Of("g1")!.Incomplete);
    }

    [Fact]
    public async Task List_sem_nada_guardado_manda_rodar_o_sync()
    {
        Prepare();

        var result = await Run("library", "characters", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf library characters sync", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_json_traz_os_jogos_e_nao_vaza_o_que_esta_escondido()
    {
        Prepare();
        await Ready();

        var result = await Run("library", "characters", "list", "--json");

        Assert.DoesNotContain("He is a ghost", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghost Sister", result.Output, StringComparison.Ordinal);
        var data = Data(result);
        Assert.Equal(2, data.GetProperty("hidden").GetInt32());
        var tomoya = data.GetProperty("characters").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "c1");
        Assert.Equal(1, tomoya.GetProperty("appearsIn").GetArrayLength());
        Assert.Equal("main", tomoya.GetProperty("appearsIn")[0].GetProperty("roleCode").GetString());
    }

    [Fact]
    public async Task Show_mostra_a_traducao_da_ficha_com_o_aparece_em_e_o_spoiler_escondido()
    {
        Prepare();
        await Ready(translate: true);

        var result = await Run("library", "characters", "show", "c1");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Tomoya Okazaki (岡崎 朋也) · c1 · masculino", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("(real:", result.Output, StringComparison.Ordinal);
        Assert.Contains("Também chamado de: Tomo", result.Output, StringComparison.Ordinal);
        Assert.Contains("Descrição (traduzido pela IA):", result.Output, StringComparison.Ordinal);
        Assert.Contains("[pt-BR] The hero. [spoiler escondido]", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("He is a ghost", result.Output, StringComparison.Ordinal);
        Assert.Contains("Traços: Cabelo longo (1 escondido por spoiler ou conteúdo adulto)", result.Output, StringComparison.Ordinal);
        Assert.Contains("CLANNAD (g1) · protagonista", result.Output, StringComparison.Ordinal);
        Assert.Contains("(mais 1 jogo escondido por spoiler; use --show-spoilers)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Outside", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_com_show_spoilers_mostra_os_dois_jogos_com_o_papel_de_cada_um()
    {
        Prepare();
        await Ready();

        var result = await Run("library", "characters", "show", "c1", "--show-spoilers");

        Assert.Contains("(real: feminino)", result.Output, StringComparison.Ordinal);
        Assert.Contains("CLANNAD (g1) · protagonista", result.Output, StringComparison.Ordinal);
        Assert.Contains("Little Busters (g2) · secundário", result.Output, StringComparison.Ordinal);
        Assert.Contains("He is a ghost", result.Output, StringComparison.Ordinal);
        Assert.Contains("Amnesia", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Character_e_o_mesmo_que_characters_show()
    {
        Prepare();
        await Ready();

        var longForm = await Run("library", "characters", "show", "c1");
        var shortForm = await Run("library", "character", "c1");

        Assert.Equal(0, shortForm.ExitCode);
        Assert.Equal(longForm.Output, shortForm.Output);
    }

    [Fact]
    public async Task Show_de_personagem_escondido_por_spoiler_ou_fora_da_biblioteca_diz_o_motivo()
    {
        Prepare();
        await Ready();

        var hidden = await Run("library", "characters", "show", "c2");
        var outside = await Run("library", "characters", "show", "c9");
        var open = await Run("library", "characters", "show", "c2", "--show-spoilers");
        var editionDoesNotLower = await Run("library", "characters", "show", "c3");

        Assert.Equal(1, editionDoesNotLower.ExitCode);
        Assert.Contains("só aparece em jogos escondidos por spoiler", editionDoesNotLower.Error, StringComparison.Ordinal);
        Assert.Equal(1, hidden.ExitCode);
        Assert.Contains("só aparece em jogos escondidos por spoiler", hidden.Error, StringComparison.Ordinal);
        Assert.Equal(1, outside.ExitCode);
        Assert.Contains("não está em nenhum jogo da biblioteca", outside.Error, StringComparison.Ordinal);
        Assert.Equal(0, open.ExitCode);
        Assert.Contains("Ghost Sister", open.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_edicao_casada_com_a_pasta_decide_o_papel_e_o_spoiler()
    {
        Prepare(secondFolder: "Little Busters Perfect Edition");
        await Run("library", "characters", "sync");

        var hiddenByEdition = await Run("library", "characters", "show", "c3");
        var open = await Run("library", "characters", "show", "c3", "--show-spoilers");

        Assert.Equal(1, hiddenByEdition.ExitCode);
        Assert.Contains("Little Busters (g2) · secundário · edição r11", open.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_edicao_casada_vale_o_papel_mais_importante_e_o_maior_spoiler()
    {
        Prepare(secondFolder: "Pasta sem relacao");
        await Run("library", "characters", "sync");

        var hidden = await Run("library", "characters", "show", "c3");
        var open = await Run("library", "characters", "show", "c3", "--show-spoilers");

        Assert.Equal(1, hidden.ExitCode);
        Assert.Contains("Little Busters (g2) · protagonista", open.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("edição r1", open.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_privacidade_da_biblioteca_vale_para_os_personagens()
    {
        Prepare();
        await Ready();
        await Run("library", "privacy", "--spoilers", "sim");

        var result = await Run("library", "characters", "list");

        Assert.Contains("3 personagens na biblioteca:", result.Output, StringComparison.Ordinal);
    }
}
