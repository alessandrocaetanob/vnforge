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

    private sealed class MatchingEngine : IEngine
    {
        public string Id => "fake";

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("parece fake");
    }
}
