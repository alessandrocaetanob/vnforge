using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Projects;
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
    public async Task Privacy_grava_a_escolha_e_a_ficha_passa_a_mostrar()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");

        var show = await Run("library", "privacy");
        var set = await Run("library", "privacy", "--spoilers", "sim", "--adult", "mostrar");
        var sheet = await Run("library", "list", "CLANNAD");
        var bad = await Run("library", "privacy", "--spoilers", "talvez");

        Assert.Contains("Spoilers: escondidos. Conteúdo adulto: desfocar", show.Output);
        Assert.Contains("Spoilers: visíveis. Conteúdo adulto: mostrar (capa, tags e imagens visíveis). Gravado.", set.Output);
        Assert.Contains("O final", sheet.Output);
        var privacy = new LibraryFile(_cli.UserConfigDirectory).Load().Privacy;
        Assert.True(privacy.ShowSpoilers);
        Assert.Equal(AdultMode.Show, privacy.Adult);
        Assert.Equal(1, bad.ExitCode);
        Assert.Contains("sim ou nao", bad.Error);
    }

    [Theory]
    [InlineData("desfocar", AdultMode.Blur)]
    [InlineData("sim", AdultMode.Show)]
    [InlineData("nao", AdultMode.Blur)]
    public async Task Privacy_adult_aceita_os_tres_modos_e_os_sim_e_nao_de_antes(string text, AdultMode expected)
    {
        var set = await Run("library", "privacy", "--adult", text);

        Assert.Equal(0, set.ExitCode);
        Assert.Equal(expected, new LibraryFile(_cli.UserConfigDirectory).Load().Privacy.Adult);
    }

    [Fact]
    public async Task Privacy_adult_recusa_um_modo_que_nao_existe_e_nao_grava()
    {
        var bad = await Run("library", "privacy", "--adult", "borrar");

        Assert.Equal(1, bad.ExitCode);
        Assert.Contains("mostrar, desfocar ou esconder", bad.Error);
        Assert.Equal(AdultMode.Blur, new LibraryFile(_cli.UserConfigDirectory).Load().Privacy.Adult);
    }

    [Fact]
    public async Task No_esconder_o_jogo_adulto_sai_da_lista_e_da_busca_e_o_show_adult_o_traz_so_nesta_chamada()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");
        var before = Data(await Run("library", "list", "--json")).GetProperty("total").GetInt32();
        await Run("library", "privacy", "--adult", "esconder");

        var list = await Run("library", "list");
        var json = Data(await Run("library", "list", "--json"));
        var byName = await Run("library", "list", "Eroge");
        var shown = await Run("library", "list", "--show-adult");

        Assert.DoesNotContain("Eroge", list.Output);
        Assert.Contains("1 jogo adulto escondido", list.Output);
        Assert.Equal(before - 1, json.GetProperty("total").GetInt32());
        Assert.Equal(1, byName.ExitCode);
        Assert.Contains("conteúdo adulto", byName.Error);
        Assert.Contains("Eroge", shown.Output);
        Assert.Equal(AdultMode.Hide, new LibraryFile(_cli.UserConfigDirectory).Load().Privacy.Adult);
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
    public async Task Link_de_jogo_movido_leva_o_projeto_para_a_pasta_da_biblioteca_ou_recusa_se_nao_for_o_mesmo_jogo()
    {
        await Run("library", "scan", Collection);
        var folder = Saved("CLANNAD").Folder;
        var project = ProjectFolder.Create(
            Path.Combine(_cli.Root, "projeto"),
            new ProjectConfig("Teste", new GameConfig(Path.Combine(_cli.Root, "antigo"), "CLANNAD.exe"), new EngineConfig("yuris"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("fold"), AiConfig.Default));
        PipelineState State(string content) => new([new OriginalFile("data.xp3", 1, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))))], new Dictionary<string, StageState>(), Path.Combine(_cli.Root, "antigo"));
        project.SaveState(State("y"));

        var refused = await Run("library", "link", "CLANNAD", "--project", project.Root);

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("data.xp3", refused.Error);
        Assert.Null(Saved("CLANNAD").ProjectPath);
        Assert.Equal(Path.Combine(_cli.Root, "antigo"), project.LoadState().GameDirectory);

        project.SaveState(State("x"));
        var linked = await Run("library", "link", "CLANNAD", "--project", project.Root);

        Assert.Equal(0, linked.ExitCode);
        Assert.Contains("passou a apontar", linked.Output);
        Assert.NotNull(Saved("CLANNAD").ProjectPath);
        Assert.Equal(folder, project.LoadState().GameDirectory);
        Assert.Equal(folder.Replace('\\', '/'), project.LoadConfig().Game.Path);
    }

    private async Task Prepare()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");
    }

    private UserTags SavedTags() => new UserTagsFile(_cli.UserConfigDirectory).Load();

    private static string[] Titles(RunResult result) =>
        [.. Data(result).GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()!).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Tag_add_normaliza_o_nome_cria_a_tag_e_grava_fora_do_library_json()
    {
        await Prepare();
        var libraryBefore = await File.ReadAllTextAsync(new LibraryFile(_cli.UserConfigDirectory).Path, Ct);

        var result = await Run("library", "tag", "add", "CLANNAD", "  Pra   TRADUZIR ", "Favorito", "favorito", "--color", "FF8800");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("adicionadas: pra traduzir, favorito.", result.Output);
        var tags = SavedTags();
        Assert.Equal(["favorito", "pra traduzir"], tags.TagsOf(Saved("CLANNAD").Id));
        Assert.Equal("#ff8800", tags.Get("favorito").Color);
        Assert.Equal("#ff8800", tags.Get("pra traduzir").Color);
        Assert.Equal(libraryBefore, await File.ReadAllTextAsync(new LibraryFile(_cli.UserConfigDirectory).Path, Ct));

        var again = Data(await Run("library", "tag", "add", "CLANNAD", "favorito", "rejogar", "--json"));
        Assert.Equal(["rejogar"], again.GetProperty("added").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["favorito"], again.GetProperty("already").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Tag_add_recusa_nome_cor_e_jogo_invalidos_sem_gravar()
    {
        await Prepare();

        var color = await Run("library", "tag", "add", "CLANNAD", "a", "--color", "azul");
        var name = await Run("library", "tag", "add", "CLANNAD", "vndb:drama");
        var game = await Run("library", "tag", "add", "Inexistente", "a");

        Assert.All(new[] { color, name, game }, r => Assert.Equal(1, r.ExitCode));
        Assert.Contains("cor", color.Error);
        Assert.Contains("dois-pontos", name.Error);
        Assert.Contains("Nenhum jogo", game.Error);
        Assert.False(File.Exists(new UserTagsFile(_cli.UserConfigDirectory).Path));
    }

    [Fact]
    public async Task Tag_remove_tira_do_jogo_e_a_tag_continua_na_lista()
    {
        await Prepare();
        await Run("library", "tag", "add", "CLANNAD", "a", "b");

        var result = await Run("library", "tag", "remove", "CLANNAD", "A", "b");
        var unknown = await Run("library", "tag", "remove", "CLANNAD", "x");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("retiradas: a, b.", result.Output);
        Assert.Empty(SavedTags().TagsOf(Saved("CLANNAD").Id));
        Assert.Equal(["a", "b"], SavedTags().Tags.Select(t => t.Name));
        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("não existe", unknown.Error);
    }

    [Fact]
    public async Task Tag_list_mostra_as_tags_com_a_contagem_de_jogos_e_as_de_um_jogo()
    {
        await Prepare();
        await Run("library", "tag", "add", "CLANNAD", "favorito", "--color", "#00ff00");
        await Run("library", "tag", "add", "Eroge", "favorito", "rejogar");

        var all = Data(await Run("library", "tag", "list", "--json"));
        var text = await Run("library", "tag", "list");
        var one = await Run("library", "tag", "list", "Eroge");
        var empty = new CliHarness();

        var favorite = all.GetProperty("tags").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "favorito");
        Assert.Equal(2, favorite.GetProperty("games").GetInt32());
        Assert.Equal("#00ff00", favorite.GetProperty("color").GetString());
        Assert.Contains("favorito", text.Output);
        Assert.Contains("Nenhum filtro por tag salvo.", text.Output);
        Assert.Contains("Eroge: favorito, rejogar.", one.Output);
        Assert.Contains("Você ainda não criou tags", (await empty.RunAsync(["library", "tag", "list"], Ct)).Output);
        empty.Dispose();
    }

    [Fact]
    public async Task Tag_rename_atualiza_todos_os_jogos_e_recusa_destino_existente_sem_merge()
    {
        await Prepare();
        await Run("library", "tag", "add", "CLANNAD", "rejogar", "favorito");
        await Run("library", "tag", "add", "Eroge", "rejogar");

        var renamed = await Run("library", "tag", "rename", "rejogar", "Rejogar em Breve");
        var refused = await Run("library", "tag", "rename", "rejogar em breve", "favorito");
        var merged = await Run("library", "tag", "rename", "rejogar em breve", "favorito", "--merge");

        Assert.Contains("'rejogar' virou 'rejogar em breve' em 2 jogos", renamed.Output);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("--merge", refused.Error);
        Assert.Contains("Tag mesclada", merged.Output);
        var tags = SavedTags();
        Assert.Equal(["favorito"], tags.Tags.Select(t => t.Name));
        Assert.Equal(["favorito"], tags.TagsOf(Saved("CLANNAD").Id));
        Assert.Equal(["favorito"], tags.TagsOf(Saved("Eroge").Id));
    }

    [Fact]
    public async Task List_salva_o_filtro_e_usa_o_salvo_depois()
    {
        await Prepare();
        await Run("library", "tag", "add", "CLANNAD", "favorito");
        await Run("library", "tag", "add", "Eroge", "favorito");

        var saved = await Run("library", "list", "--tag", "favorito", "--not", "vndb:cena explícita", "--save");
        var used = await Run("library", "list", "--saved", "--json");
        var shown = await Run("library", "tag", "list");
        var clash = await Run("library", "list", "--saved", "--tag", "x");
        var cleared = await Run("library", "list", "--save");
        var after = await Run("library", "list", "--saved", "--json");

        Assert.Equal(0, saved.ExitCode);
        Assert.Contains("Filtro por tag: com todas de favorito; sem (tag escondida). Salvo.", saved.Output);
        Assert.Equal(["CLANNAD", "Eroge"], Titles(used));
        Assert.Contains("Filtro salvo: com todas de favorito; sem (tag escondida).", shown.Output);
        Assert.Equal(1, clash.ExitCode);
        Assert.Contains("--saved", clash.Error);
        Assert.Contains("Filtro por tag salvo apagado.", cleared.Output);
        Assert.Equal(4, Titles(after).Length);
    }

    [Fact]
    public async Task List_recusa_any_sem_tag_e_filtro_com_jogo_na_ficha()
    {
        await Prepare();

        var any = await Run("library", "list", "--any");
        var sheet = await Run("library", "list", "CLANNAD", "--tag", "x");

        Assert.Equal(1, any.ExitCode);
        Assert.Contains("--any", any.Error);
        Assert.Equal(1, sheet.ExitCode);
        Assert.Contains("tag list", sheet.Error);
    }

    [Fact]
    public async Task Renomear_e_apagar_acompanham_o_filtro_salvo()
    {
        await Prepare();
        await Run("library", "tag", "add", "CLANNAD", "a", "b");
        await Run("library", "list", "--tag", "minha:a", "--not", "b", "--save");

        await Run("library", "tag", "rename", "a", "z");
        Assert.Equal(["minha:z"], SavedTags().SavedFilter.Include);

        await Run("library", "tag", "delete", "b", "--yes");
        Assert.Empty(SavedTags().SavedFilter.Exclude);
    }

    [Fact]
    public async Task Filtro_salvo_com_tag_do_vndb_escondida_aparece_mascarado_sem_a_opcao()
    {
        await Prepare();
        await Run("library", "list", "--tag", "vndb:o final", "--not", "vndb:cena explícita", "--show-spoilers", "--show-adult", "--save");

        var hiddenList = await Run("library", "list", "--saved");
        var hiddenJson = Data(await Run("library", "list", "--saved", "--json")).GetProperty("filter");
        var hiddenTagList = await Run("library", "tag", "list");
        var hiddenTagJson = Data(await Run("library", "tag", "list", "--json")).GetProperty("filter");
        var shown = await Run("library", "list", "--saved", "--show-spoilers", "--show-adult");

        Assert.Contains("(tag escondida)", hiddenList.Output);
        Assert.DoesNotContain("o final", hiddenList.Output);
        Assert.DoesNotContain("explícita", hiddenList.Output);
        Assert.Equal(["(tag escondida)"], hiddenJson.GetProperty("include").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["(tag escondida)"], hiddenJson.GetProperty("exclude").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("(tag escondida)", hiddenTagList.Output);
        Assert.DoesNotContain("o final", hiddenTagList.Output);
        Assert.Equal(["(tag escondida)"], hiddenTagJson.GetProperty("include").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("vndb:o final", shown.Output);
    }

    [Fact]
    public async Task Ficha_e_json_do_jogo_trazem_as_tags_proprias()
    {
        await Prepare();
        await Run("library", "tag", "add", "CLANNAD", "favorito");

        var sheet = await Run("library", "list", "CLANNAD");
        var json = Data(await Run("library", "list", "CLANNAD", "--json"));

        Assert.Contains("Minhas tags: favorito", sheet.Output);
        Assert.Equal(["favorito"], json.GetProperty("game").GetProperty("userTags").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Tags_ilegiveis_recusam_e_nao_sao_sobrescritas()
    {
        await Prepare();
        var file = new UserTagsFile(_cli.UserConfigDirectory);
        await File.WriteAllTextAsync(file.Path, "{ nao e json", Ct);

        var add = await Run("library", "tag", "add", "CLANNAD", "a");
        var list = await Run("library", "list");

        Assert.Equal(1, add.ExitCode);
        Assert.Equal(1, list.ExitCode);
        Assert.Contains("library-tags.json", add.Error);
        Assert.Equal("{ nao e json", await File.ReadAllTextAsync(file.Path, Ct));
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

    [Fact]
    public async Task Match_nao_perde_o_que_outro_processo_gravou_enquanto_buscava()
    {
        var inner = UseFakeCatalog();
        var file = new LibraryFile(_cli.UserConfigDirectory);
        var intruder = new LibraryGame(
            GameLibrary.IdFor(Path.Combine(_cli.Root, "outro")),
            Path.Combine(_cli.Root, "outro"),
            null,
            LibraryEngine.Unknown,
            "Jogo de outro processo",
            null,
            DateTimeOffset.UnixEpoch);
        var calls = 0;
        _cli.Cli.VndbCatalogFactory = () => new BusyCatalog(inner, () =>
        {
            if (++calls == 1)
            {
                file.Update(l =>
                {
                    l.Privacy = new LibraryPrivacy(Adult: AdultMode.Show);
                    l.Merge([intruder], _cli.Root);
                });
            }
        });
        await Run("library", "scan", Collection);

        var result = await Run("library", "match", "--auto");

        Assert.Equal(0, result.ExitCode);
        var library = file.Load();
        Assert.True(library.Privacy.ShowAdult);
        Assert.Contains(library.Games, g => g.Title == "Jogo de outro processo");
        Assert.Equal(["CLANNAD", "Eroge", "Steins;Gate"], library.Games.Where(g => g.IsConfirmed).Select(g => g.Title).Order(StringComparer.Ordinal));
    }

    private sealed class BusyCatalog(IVndbCatalog inner, Action onSearch) : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default)
        {
            onSearch();
            return inner.SearchVisualNovelsAsync(title, limit, ct);
        }

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => inner.GetVisualNovelAsync(id, ct);

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => inner.GetCharactersAsync(visualNovelId, ct);

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => inner.SearchCharactersByTitleAsync(title, ct);

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => inner.GetReleasesAsync(visualNovelId, ct);
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
