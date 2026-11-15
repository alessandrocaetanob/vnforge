using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R9")]
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

    private async Task Prepare()
    {
        UseFakeCatalog();
        await Run("library", "scan", Collection);
        await Run("library", "match", "--auto");
    }

    private UserTags SavedTags() => new UserTagsFile(_cli.UserConfigDirectory).Load();

    private static string[] Titles(RunResult result) =>
        [.. Data(result).GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()!).Order(StringComparer.Ordinal)];
}
