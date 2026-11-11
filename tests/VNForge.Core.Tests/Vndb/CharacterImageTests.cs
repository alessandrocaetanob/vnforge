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
    public void O_id_e_a_imagem_fazem_a_ida_e_volta()
    {
        var entry = new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male, VndbId: "c1", Image: KentaImage);

        var back = Assert.Single(CharacterSerializer.Parse(CharacterSerializer.Serialize([entry]), "characters.json"));

        Assert.Equal("c1", back.VndbId);
        Assert.Equal(KentaImage, back.Image);
    }

    [Fact]
    public async Task O_vndb_json_com_imagem_faz_a_ida_e_volta()
    {
        var link = await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        var back = VndbLinkSerializer.Parse(VndbLinkSerializer.Serialize(link), "vndb.json");

        Assert.Equal(KentaImage, back.Characters[0].Image);
        Assert.Null(back.Characters[1].Image);
    }
}
