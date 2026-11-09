using System.Text.Json;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

/// <summary>#536: <c>vnf vndb</c> e <c>vnf init --vndb</c> ligam o projeto ao jogo do VNDB, com o catálogo falso e um jogo sintético (nunca a API nem arquivo de jogo).</summary>
public sealed class VndbCommandTests : IDisposable
{
    private const string GameTitle = "雨音の箱庭";
    private const string GameId = "v900001";
    private const string JsonFlag = "--json";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public VndbCommandTests()
    {
        _game = Path.Combine(_cli.Root, "AmaotoNoHakoniwa");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "Hakoniwa.exe"), "MZ");
        var script = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        script.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        script.AddCommand("GOSUB", 3, script.StringExpression("ES.CHAR.NAME"), script.StringExpression("美咲"));
        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").Build();
        File.WriteAllBytes(
            Path.Combine(_game, "ysbin.ypf"),
            new SyntheticYpfBuilder(500)
                .AddFile("ysc.ybn", Yscm.Bytes)
                .AddFile("yst_list.ybn", list.Bytes)
                .AddFile("yst00001.ybn", script.Build().Bytes)
                .AddFile("ysbin/yscfg.ybn", SyntheticYscfgBuilder.Build(GameTitle))
                .Build().Bytes);
        _cli.Cli.VndbCatalogFactory = () => Catalog;
    }

    private FakeVndbCatalog Catalog { get; } = new(
        [Game(GameId, "Amaoto no Hakoniwa", GameTitle), Game("v900002", "Amaoto no Hakoniwa: Fan Disc", "雨音の箱庭 ファンディスク")],
        [
            Character("c1", "Kenta Aizawa", "相沢 健太", "m", "main"),
            Character("c2", "Misaki Todo", "藤堂 美咲", "f"),
            Character("c3", "Sem par no jogo", "誰某", "f"),
        ]);

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "projeto");

    private static VndbVisualNovel Game(string id, string title, string original) =>
        new(id, title, original, [], [], "ja", 0, "2019-05-31", ["ja"], ["win"], null, 3, 1080, 10, "Numa cidade [b]onde chove[/b].", []);

    private static VndbCharacter Character(string id, string name, string original, string gender, string role = "side") =>
        new(id, name, original, [], "Descrição de " + name, null, new VndbGender(gender, gender), [new VndbCharacterAppearance(GameId, role, 0, null)], []);

    [Fact]
    public async Task Search_com_a_pasta_do_jogo_le_o_titulo_do_yscfg_e_poe_na_frente_o_que_combina_com_a_pasta()
    {
        var result = await _cli.RunAsync(["vndb", "search", "--game", _game], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("2 jogos achados no VNDB para “雨音の箱庭”", result.Output, StringComparison.Ordinal);
        Assert.Contains("v900001 Amaoto no Hakoniwa (雨音の箱庭) · 2019 · 18 h · combina com a pasta e o exe", result.Output, StringComparison.Ordinal);
        Assert.Equal(["Search(雨音の箱庭)"], Catalog.Calls.Select(c => c.Replace("SearchVisualNovelsAsync", "Search")));
    }

    [Fact]
    public async Task Search_com_json_entrega_os_resultados_e_se_combinam_com_a_pasta()
    {
        var result = await _cli.RunAsync(["vndb", "search", "Fan Disc", JsonFlag], Token);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var found = json.RootElement.GetProperty("data").GetProperty("results").EnumerateArray().Single();
        Assert.Equal("v900002", found.GetProperty("id").GetString());
        Assert.False(found.GetProperty("matchesFolder").GetBoolean());
    }

    [Fact]
    public async Task Search_sem_titulo_nem_pasta_pede_um_dos_dois()
    {
        var result = await _cli.RunAsync(["vndb", "search"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--game", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_sem_resultado_diz_que_nao_achou()
    {
        var result = await _cli.RunAsync(["vndb", "search", "Titulo que ninguem tem"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("não achou", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Init_com_vndb_liga_o_jogo_e_extrair_leva_os_personagens_com_genero_para_o_characters_json()
    {
        var init = await _cli.RunAsync(["init", "projeto", "--game", _game, "--vndb", GameId], Token);

        Assert.Equal(0, init.ExitCode);
        Assert.Contains("Jogo do VNDB: Amaoto no Hakoniwa (v900001), 3 personagens", init.Output, StringComparison.Ordinal);
        var project = ProjectFolder.Open(ProjectPath);
        Assert.Equal("Numa cidade onde chove.", project.LoadVndbLink()?.Synopsis);
        Assert.False(File.Exists(project.CharactersPath));

        var extract = await _cli.RunAsync(["extract", "--project", ProjectPath], Token);

        Assert.Equal(0, extract.ExitCode);
        var misaki = Assert.Single(project.LoadCharacters());
        Assert.Equal(("美咲", CharacterGender.Female, "Misaki Todo"), (misaki.Source, misaki.Gender, misaki.Romaji));
    }

    [Fact]
    public async Task Init_com_id_que_o_vndb_nao_tem_recusa_sem_criar_o_projeto()
    {
        var result = await _cli.RunAsync(["init", "projeto", "--game", _game, "--vndb", "v1"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("v1", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(ProjectPath, "vnforge.json")));
    }

    [Fact]
    public async Task Link_num_projeto_que_ja_foi_extraido_leva_os_personagens_na_hora()
    {
        Assert.Equal(0, (await _cli.RunAsync(["init", "projeto", "--game", _game], Token)).ExitCode);
        Assert.Equal(0, (await _cli.RunAsync(["extract", "--project", ProjectPath], Token)).ExitCode);

        var link = await _cli.RunAsync(["vndb", "link", GameId, "--project", ProjectPath], Token);

        Assert.Equal(0, link.ExitCode);
        Assert.Contains("1 casaram com os nomes do jogo: 1 entraram em characters.json", link.Output, StringComparison.Ordinal);
        Assert.Equal(CharacterGender.Female, Assert.Single(ProjectFolder.Open(ProjectPath).LoadCharacters()).Gender);
    }

    [Fact]
    public async Task Show_mostra_o_jogo_ligado_e_sem_ligacao_orienta_o_link()
    {
        Assert.Equal(0, (await _cli.RunAsync(["init", "projeto", "--game", _game], Token)).ExitCode);

        var none = await _cli.RunAsync(["vndb", "show", "--project", ProjectPath], Token);
        Assert.Equal(1, none.ExitCode);
        Assert.Contains("vnf vndb link", none.Error, StringComparison.Ordinal);

        await _cli.RunAsync(["vndb", "link", GameId, "--project", ProjectPath], Token);
        var shown = await _cli.RunAsync(["vndb", "show", "--project", ProjectPath, JsonFlag], Token);

        using var json = JsonDocument.Parse(shown.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(GameId, data.GetProperty("id").GetString());
        Assert.Equal(3, data.GetProperty("characters").GetInt32());
    }
}
