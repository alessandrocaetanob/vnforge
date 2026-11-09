using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

public sealed class LibraryCommandTests : IDisposable
{
    // YPF v500 vazio: o YU-RIS o reconhece pelo IEngine.Detect, sem nenhum arquivo de jogo de verdade.
    private static readonly byte[] EmptyYpf = Convert.FromHexString(
        "59504600" + "F4010000" + "00000000" + "20000000" + "00000000000000000000000000000000");

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Collection
    {
        get
        {
            var root = Path.Combine(_cli.Root, "colecao");
            Touch(root, "[Key] CLANNAD v1.02", "data.xp3");
            Touch(root, "[Key] CLANNAD v1.02", "CLANNAD.exe");
            Touch(root, "Steins;Gate", "Scene.pck");
            Touch(root, "Eroge", "game", "script.rpa");
            Touch(root, "Grupo", "Sakura no Uta", "bgm.ypf");
            File.WriteAllBytes(Path.Combine(root, "Grupo", "Sakura no Uta", "bgm.ypf"), EmptyYpf);
            Touch(root, "Windows", "Sistema", "data.xp3");
            Touch(root, "Anotações", "notas.txt");
            return root;
        }
    }

    private static void Touch(params string[] parts)
    {
        var path = Path.Combine(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static VndbVisualNovel Vn(string id, string title, string? alt = null, VndbImage? image = null, IReadOnlyList<VndbTag>? tags = null, string? description = null) =>
        new(id, title, alt, [], [], "ja", 0, "2004-04-28", ["ja"], ["win"], image, 5, 4000, 10, description, tags ?? []);

    private FakeVndbCatalog UseFakeCatalog()
    {
        var catalog = new FakeVndbCatalog(
        [
            Vn("v4", "CLANNAD", "クラナド", new VndbImage("https://example.test/clannad.jpg", null, null, null, 0, 0), [new VndbTag("g1", "Drama", "cont", 2.9, 0, false), new VndbTag("g2", "O final", "cont", 2.0, 2, false)], "Sinopse [spoiler]final[/spoiler]."),
            Vn("v17", "Steins;Gate"),
            Vn("v50", "Eroge", null, new VndbImage("https://example.test/eroge.jpg", null, null, null, 1.9, 0), [new VndbTag("g9", "Cena explícita", "ero", 2.8, 0, false)]),
        ]);
        _cli.Cli.VndbCatalogFactory = () => catalog;
        return catalog;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private LibraryGame Saved(string reference) => new LibraryFile(_cli.UserConfigDirectory).Load().Find(reference);

    [Fact]
    public async Task Scan_reconhece_as_engines_grava_a_biblioteca_e_pula_o_que_nao_e_jogo()
    {
        var result = await Run("library", "scan", Collection);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("4 jogos reconhecidos", result.Output);
        Assert.Contains("KiriKiri 1", result.Output);
        Assert.Contains("YU-RIS 1", result.Output);
        Assert.Contains("vnf library match", result.Output);
        var library = new LibraryFile(_cli.UserConfigDirectory).Load();
        Assert.Equal(4, library.Games.Count);
        Assert.Equal(
            ["kirikiri", "renpy", "siglus", "yuris"],
            library.Games.Select(g => g.Engine.Id).Order(StringComparer.Ordinal));
        Assert.Equal(LibraryEngineSource.Engine, library.Games.Single(g => g.Engine.Id == "yuris").Engine.Source);
    }

    [Fact]
    public async Task Scan_json_traz_novos_conhecidos_e_o_total()
    {
        var folder = Collection;

        var first = Data(await Run("library", "scan", folder, "--json"));
        var second = Data(await Run("library", "scan", folder, "--json"));

        Assert.Equal(4, first.GetProperty("found").GetInt32());
        Assert.Equal(4, first.GetProperty("added").GetArrayLength());
        Assert.Equal(0, second.GetProperty("added").GetArrayLength());
        Assert.Equal(4, second.GetProperty("unchanged").GetInt32());
        Assert.Equal(4, second.GetProperty("total").GetInt32());
        Assert.Equal(0, second.GetProperty("missing").GetArrayLength());
    }

    [Fact]
    public async Task Scan_de_pasta_que_nao_existe_e_erro_esperado()
    {
        var result = await Run("library", "scan", Path.Combine(_cli.Root, "nada"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("não existe", result.Error);
    }

    [Fact]
    public async Task Scan_cancelado_sai_com_codigo_de_cancelamento_e_guarda_o_que_achou()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await source.CancelAsync();

        var result = await _cli.RunAsync(["library", "scan", Collection], source.Token);

        Assert.Equal(3, result.ExitCode);
        Assert.True(File.Exists(new LibraryFile(_cli.UserConfigDirectory).Path));
    }

    [Fact]
    public async Task Match_auto_confirma_o_que_nao_deixa_duvida_e_nao_chama_a_rede()
    {
        var catalog = UseFakeCatalog();
        await Run("library", "scan", Collection);

        var result = await Run("library", "match", "--auto", "--json");

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.Equal(["CLANNAD", "Eroge", "Steins;Gate"], data.GetProperty("confirmed").EnumerateArray().Select(g => g.GetProperty("title").GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(["Sakura no Uta"], data.GetProperty("notFound").EnumerateArray().Select(g => g.GetProperty("title").GetString()));
        Assert.All(catalog.Calls, call => Assert.StartsWith("SearchVisualNovelsAsync(", call, StringComparison.Ordinal));
        var clannad = Saved("CLANNAD");
        Assert.Equal("v4", clannad.VndbId);
        Assert.Equal(LibraryConfirmation.Auto, clannad.Confirmation);
        Assert.Equal("クラナド", clannad.Info?.OriginalTitle);
    }

    [Fact]
    public async Task Match_sem_auto_so_propoe_candidatos_e_depois_confirma_a_mao()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);

        var proposed = await Run("library", "match");
        var confirmed = await Run("library", "match", "--confirm", "CLANNAD", "v4");

        Assert.Equal(0, proposed.ExitCode);
        Assert.Contains("0 confirmados, 3 com candidatos", proposed.Output);
        Assert.Contains("Escolha um: [Key] CLANNAD v1.02", proposed.Output);
        Assert.Contains("v4  CLANNAD (クラナド), 2004", proposed.Output);
        Assert.Contains("vnf library match --confirm", proposed.Output);
        Assert.Contains("Open Database License", proposed.Output);
        Assert.Equal(0, confirmed.ExitCode);
        Assert.Contains("confirmado como v4 CLANNAD", confirmed.Output);
        var game = Saved("CLANNAD");
        Assert.Equal(LibraryConfirmation.Manual, game.Confirmation);
        Assert.Equal("v4", game.VndbId);
    }

    [Fact]
    public async Task Match_de_novo_nao_busca_quem_ja_foi_buscado()
    {
        var catalog = UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match");
        var calls = catalog.Calls.Count;

        var second = await Run("library", "match");

        Assert.Contains("Nada para casar", second.Output);
        Assert.Equal(calls, catalog.Calls.Count);
    }

    [Fact]
    public async Task Match_confirm_com_id_que_o_vndb_nao_tem_ou_mal_escrito_falha_com_mensagem()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);

        var missing = await Run("library", "match", "--confirm", "CLANNAD", "v999");
        var malformed = await Run("library", "match", "--confirm", "CLANNAD", "abc");

        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("O VNDB não tem o jogo v999", missing.Error);
        Assert.Equal(1, malformed.ExitCode);
        Assert.Contains("Id do VNDB inválido", malformed.Error);
        Assert.False(Saved("CLANNAD").IsConfirmed);
    }

    [Fact]
    public async Task Match_reset_desfaz_a_confirmacao()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var result = await Run("library", "match", "--reset", "CLANNAD");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("confirmação de v4 foi desfeita", result.Output);
        Assert.False(Saved("CLANNAD").IsConfirmed);
    }

    [Fact]
    public async Task Match_recusa_modos_que_se_misturam()
    {
        var result = await Run("library", "match", "--auto", "--reset", "CLANNAD");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Use só uma coisa por vez", result.Error);
    }

    [Fact]
    public async Task Match_com_a_biblioteca_vazia_manda_varrer_primeiro()
    {
        var result = await Run("library", "match");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf library scan", result.Output);
    }

    [Fact]
    public async Task Falha_do_vndb_sai_com_codigo_de_erro_e_deixa_o_jogo_para_a_proxima()
    {
        _cli.Cli.VndbCatalogFactory = () => new OfflineCatalog();
        await Run("library", "scan", Collection);

        var result = await Run("library", "match", "--auto");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Falhou:", result.Output);
        Assert.Contains("sem rede", result.Output);
        Assert.All(new LibraryFile(_cli.UserConfigDirectory).Load().Games, g => Assert.Null(g.SearchedAt));
    }

    [Fact]
    public async Task List_mostra_a_tabela_com_o_estado_do_casamento()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var result = await Run("library", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CLANNAD", result.Output);
        Assert.Contains("v4 (auto)", result.Output);
        Assert.Contains("não achado", result.Output);
        Assert.Contains("4 jogos na biblioteca", result.Output);
    }

    [Fact]
    public async Task List_filtra_por_engine_e_por_estado()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var byEngine = Data(await Run("library", "list", "--engine", "siglus", "--json"));
        var unmatched = Data(await Run("library", "list", "--state", "unmatched", "--json"));
        var bad = await Run("library", "list", "--state", "qualquer");

        Assert.Equal(["Steins;Gate"], byEngine.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()));
        Assert.Equal(["Sakura no Uta"], unmatched.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()));
        Assert.Equal(1, bad.ExitCode);
        Assert.Contains("Estado desconhecido", bad.Error);
    }

    [Fact]
    public async Task Ficha_esconde_spoiler_e_capa_adulta_por_padrao_e_mostra_com_as_opcoes()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var hidden = await Run("library", "list", "CLANNAD");
        var spoilers = await Run("library", "list", "CLANNAD", "--show-spoilers");
        var adultHidden = await Run("library", "list", "Eroge");
        var adultShown = await Run("library", "list", "Eroge", "--show-adult");

        Assert.Contains("Tags: Drama", hidden.Output);
        Assert.DoesNotContain("O final", hidden.Output);
        Assert.Contains("1 tag escondida", hidden.Output);
        Assert.Contains("[spoiler escondido]", hidden.Output);
        Assert.Contains("https://example.test/clannad.jpg", hidden.Output);
        Assert.Contains("O final", spoilers.Output);
        Assert.Contains("final", spoilers.Output);
        Assert.DoesNotContain("[spoiler escondido]", spoilers.Output);
        Assert.Contains("Capa: escondida", adultHidden.Output);
        Assert.DoesNotContain("example.test/eroge.jpg", adultHidden.Output);
        Assert.DoesNotContain("Cena explícita", adultHidden.Output);
        Assert.Contains("https://example.test/eroge.jpg", adultShown.Output);
        Assert.Contains("Cena explícita", adultShown.Output);
    }

    [Fact]
    public async Task Json_da_ficha_respeita_a_privacidade_nos_dados()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var data = Data(await Run("library", "list", "Eroge", "--json"));
        var card = data.GetProperty("game").GetProperty("card");

        Assert.False(data.GetProperty("privacy").GetProperty("showAdult").GetBoolean());
        Assert.True(card.GetProperty("coverHidden").GetBoolean());
        Assert.False(card.TryGetProperty("coverUrl", out _));
        Assert.Equal(0, card.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task Privacy_grava_a_escolha_e_a_ficha_passa_a_mostrar()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var show = await Run("library", "privacy");
        var set = await Run("library", "privacy", "--spoilers", "sim", "--adult", "sim");
        var sheet = await Run("library", "list", "CLANNAD");
        var bad = await Run("library", "privacy", "--spoilers", "talvez");

        Assert.Contains("Spoilers: escondidos. Conteúdo adulto: escondido.", show.Output);
        Assert.Contains("Spoilers: visíveis. Conteúdo adulto: visível. Gravado.", set.Output);
        Assert.Contains("O final", sheet.Output);
        var privacy = new LibraryFile(_cli.UserConfigDirectory).Load().Privacy;
        Assert.True(privacy.ShowSpoilers);
        Assert.True(privacy.ShowAdult);
        Assert.Equal(1, bad.ExitCode);
        Assert.Contains("sim ou nao", bad.Error);
    }

    [Fact]
    public async Task Link_liga_e_desliga_o_projeto_de_traducao()
    {
        await Run("library", "scan", Collection);
        var project = Path.Combine(_cli.Root, "projeto");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "vnforge.json"), "{}", Ct);

        var linked = await Run("library", "link", "CLANNAD", "--project", project);
        var removed = await Run("library", "link", "CLANNAD", "--remove");
        var neither = await Run("library", "link", "CLANNAD");
        var notProject = await Run("library", "link", "CLANNAD", "--project", _cli.Root);

        Assert.Equal(0, linked.ExitCode);
        Assert.Contains("ligado ao projeto", linked.Output);
        Assert.Equal(0, removed.ExitCode);
        Assert.Null(Saved("CLANNAD").ProjectPath);
        Assert.Equal(1, neither.ExitCode);
        Assert.Contains("--project", neither.Error);
        Assert.Equal(1, notProject.ExitCode);
        Assert.Contains("não é um projeto", notProject.Error);
    }

    [Fact]
    public async Task Biblioteca_ilegivel_recusa_e_nao_e_sobrescrita()
    {
        var file = new LibraryFile(_cli.UserConfigDirectory);
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        await File.WriteAllTextAsync(file.Path, "{ nao e json", Ct);

        var result = await Run("library", "scan", Collection);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("library.json", result.Error);
        Assert.Equal("{ nao e json", await File.ReadAllTextAsync(file.Path, Ct));
    }

    private sealed class OfflineCatalog : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) =>
            throw new VndbException("sem rede");

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => throw new VndbException("sem rede");

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => throw new VndbException("sem rede");

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => throw new VndbException("sem rede");

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => throw new VndbException("sem rede");
    }
}
