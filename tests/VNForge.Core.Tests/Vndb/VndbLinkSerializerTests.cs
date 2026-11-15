using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

[Trait("Risco", "R3")]
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
    public void Parse_recusa_em_portugues_arquivo_fora_do_formato(string json)
    {
        var ex = Assert.Throws<VndbLinkException>(() => VndbLinkSerializer.Parse(json, "vndb.json"));

        Assert.Contains("vndb.json", ex.Message, StringComparison.Ordinal);
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
