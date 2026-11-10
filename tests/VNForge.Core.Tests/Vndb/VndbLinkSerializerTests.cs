using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

public sealed class VndbLinkSerializerTests
{
    private static readonly VndbLink Link = new(
        "v900001",
        "Amaoto no Hakoniwa",
        "雨音の箱庭",
        "Numa cidade onde chove.",
        [new VndbLinkedCharacter("c1", "Kenta Aizawa", "相沢 健太", ["Ken"], CharacterGender.Male, null, "main")],
        true);

    private static MatchingEngine Engine() => new();

    [Fact]
    public void Serialize_e_Parse_voltam_ao_mesmo_jogo_com_o_japones_sem_escape()
    {
        var json = VndbLinkSerializer.Serialize(Link);

        Assert.Contains("雨音の箱庭", json, StringComparison.Ordinal);
        Assert.Contains("\"gender\": \"male\"", json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', json);
        var back = VndbLinkSerializer.Parse(json, "vndb.json");
        Assert.Equal(Link.Characters[0], back.Characters[0] with { Aliases = Link.Characters[0].Aliases });
        Assert.Equal((Link.VisualNovelId, Link.Synopsis, Link.CharactersTruncated), (back.VisualNovelId, back.Synopsis, back.CharactersTruncated));
    }

    [Fact]
    public void A_capa_do_jogo_volta_do_arquivo_e_um_arquivo_sem_capa_continua_valido()
    {
        var withCover = Link with { Cover = new VndbImageRef("https://t.vndb.org/cv/12/345.jpg", 0.2, 0.1) };

        var json = VndbLinkSerializer.Serialize(withCover);

        Assert.Equal(withCover.Cover, VndbLinkSerializer.Parse(json, "vndb.json").Cover);
        var without = VndbLinkSerializer.Serialize(Link);
        Assert.DoesNotContain("cover", without, StringComparison.Ordinal);
        Assert.Null(VndbLinkSerializer.Parse(without, "vndb.json").Cover);
    }

    [Theory]
    [InlineData("{ nao e json")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"link\":{\"campoNovo\":1}}")]
    [InlineData("{\"version\":2,\"link\":{\"visualNovelId\":\"v1\",\"title\":\"x\",\"characters\":[]}}")]
    public void Parse_recusa_em_portugues_arquivo_fora_do_formato(string json)
    {
        var ex = Assert.Throws<VndbLinkException>(() => VndbLinkSerializer.Parse(json, "vndb.json"));

        Assert.Contains("vndb.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectFolder_sem_arquivo_devolve_nulo_e_guarda_o_que_recebeu()
    {
        using var folder = new TempFolder();
        var project = ProjectFolder.Create(folder.Path, new ProjectConfig(
            "x", new GameConfig("D:/x", "x.exe"), new EngineConfig("yuris", 500, null), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("fold"), AiConfig.Default));
        Assert.Null(project.LoadVndbLink());

        project.SaveVndbLink(Link);

        Assert.Equal("v900001", project.LoadVndbLink()?.VisualNovelId);
    }

    [Fact]
    public void Initialize_com_o_jogo_do_vndb_grava_o_vndb_json_e_sem_ele_nao_cria_o_arquivo()
    {
        using var folder = new TempFolder();
        var game = Path.Combine(folder.Path, "jogo");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "x.exe"), "MZ");

        var withLink = ProjectInitializer.Initialize(Path.Combine(folder.Path, "a"), game, [Engine()], new ProjectInitOptions { Vndb = Link });
        var without = ProjectInitializer.Initialize(Path.Combine(folder.Path, "b"), game, [Engine()]);

        Assert.Equal("Amaoto no Hakoniwa", withLink.Folder.LoadVndbLink()?.Title);
        Assert.False(File.Exists(without.Folder.VndbPath));
    }

    private sealed class MatchingEngine : IEngine
    {
        public string Id => "fake";

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("parece fake");
    }
}
