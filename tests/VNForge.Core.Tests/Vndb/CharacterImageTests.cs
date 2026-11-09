using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>O id do VNDB e a imagem do personagem em <c>vndb.json</c> e <c>characters.json</c> (#580): opcionais, e o arquivo antigo continua abrindo.</summary>
public sealed class CharacterImageTests
{
    private static readonly VndbImageRef KentaImage = new("https://t.vndb.org/ch/12/3412.jpg", 0, 0.3);

    private static FakeVndbCatalog Catalog() =>
        new([VndbSamples.Game()], [
            VndbSamples.Character("c1", "Kenta Aizawa", "相沢 健太", "m", "main", image: new VndbImage("https://t.vndb.org/ch/12/3412.jpg", null, 256, 300, 0, 0.3)),
            VndbSamples.Character("c2", "Aoi Sakura", "佐倉 葵", "f"),
        ]);

    private static ProjectFolder NewProject(string root) => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Name(int n, string source) =>
        new($"yst00001:{n:D6}", TextUnitKind.Name, null, source, null, TranslationStatus.Untranslated);

    [Fact]
    public void Os_campos_novos_ficam_fora_do_json_quando_nulos()
    {
        var json = CharacterSerializer.Serialize([new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male)]);

        Assert.DoesNotContain("vndbId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("image", json, StringComparison.Ordinal);
    }

    [Fact]
    public void O_id_e_a_imagem_fazem_a_ida_e_volta()
    {
        var entry = new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male, VndbId: "c1", Image: KentaImage);

        var back = Assert.Single(CharacterSerializer.Parse(CharacterSerializer.Serialize([entry]), "characters.json"));

        Assert.Equal("c1", back.VndbId);
        Assert.Equal(KentaImage, back.Image);
    }

    [Fact]
    public void Arquivo_sem_o_id_e_a_imagem_continua_abrindo()
    {
        const string old = "{\n  \"version\": 1,\n  \"entries\": [\n    {\"source\":\"凛\",\"target\":\"Rin\",\"gender\":\"female\",\"romaji\":\"Rin\"}\n  ]\n}\n";

        var entry = Assert.Single(CharacterSerializer.Parse(old, "characters.json"));

        Assert.Null(entry.VndbId);
        Assert.Null(entry.Image);
    }

    [Fact]
    public async Task O_personagem_ligado_leva_a_imagem_do_vndb_e_o_que_nao_tem_fica_sem()
    {
        var link = await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        Assert.Equal(KentaImage, link.Characters[0].Image);
        Assert.Null(link.Characters[1].Image);
    }

    [Fact]
    public async Task O_vndb_json_com_imagem_faz_a_ida_e_volta()
    {
        var link = await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        var back = VndbLinkSerializer.Parse(VndbLinkSerializer.Serialize(link), "vndb.json");

        Assert.Equal(KentaImage, back.Characters[0].Image);
        Assert.Null(back.Characters[1].Image);
    }

    [Fact]
    public void O_vndb_json_antigo_sem_imagem_continua_abrindo()
    {
        const string old = """
            {"version":1,"link":{"visualNovelId":"v1","title":"Jogo","characters":[
              {"id":"c1","name":"Kenta Aizawa","original":"相沢 健太","aliases":["Ken"],"gender":"male","role":"main"}]}}
            """;

        var link = VndbLinkSerializer.Parse(old, "vndb.json");

        Assert.Null(Assert.Single(link.Characters).Image);
    }

    [Fact]
    public async Task ApplyCharacters_leva_o_id_e_a_imagem_para_characters_json()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太"), Name(1, "佐倉葵")]);

        await VndbProjectLinker.LinkAsync(project, Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        var saved = project.LoadCharacters();
        Assert.Equal(("c1", KentaImage), (saved[0].VndbId, saved[0].Image));
        Assert.Equal(("c2", null), (saved[1].VndbId, saved[1].Image));
    }

    [Fact]
    public async Task ApplyCharacters_nao_troca_o_id_nem_a_imagem_que_o_personagem_ja_tem_e_preenche_quando_faltam()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太"), Name(1, "佐倉葵")]);
        var mine = new VndbImageRef("https://t.vndb.org/ch/99/1.jpg");
        project.SaveCharacters([new CharacterEntry("相沢健太", "Kenta", VndbId: "c999", Image: mine), new CharacterEntry("佐倉葵", "Aoi", Romaji: "Aoi Sakura")]);
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));

        var result = VndbProjectLinker.ApplyCharacters(project);

        var saved = project.LoadCharacters();
        Assert.Equal(("c999", mine), (saved[0].VndbId, saved[0].Image));
        Assert.Equal("c2", saved[1].VndbId);
        Assert.Equal(new VndbApplyResult(2, 0, 2), result);
    }

    [Fact]
    public async Task Ligar_de_novo_um_projeto_que_ja_tinha_os_personagens_sem_id_preenche_so_o_id_e_a_imagem()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太")]);
        project.SaveCharacters([new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male, Romaji: "Aizawa Kenta", Aliases: ["Ken"], Description: "O protagonista.")]);
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));

        VndbProjectLinker.ApplyCharacters(project);

        var entry = Assert.Single(project.LoadCharacters());
        Assert.Equal(("c1", KentaImage, "Aizawa Kenta"), (entry.VndbId, entry.Image, entry.Romaji));
    }
}
