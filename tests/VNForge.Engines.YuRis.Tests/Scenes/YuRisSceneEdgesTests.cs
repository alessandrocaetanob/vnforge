using VNForge.Core.Engines;
using VNForge.Core.Scenes;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Yslb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Yslb;

namespace VNForge.Engines.YuRis.Tests.Scenes;

[Trait("Risco", "R7")]
public sealed class YuRisSceneEdgesTests : IDisposable
{
    private const string Gosub = "GOSUB";
    private const string Go = "GO";
    private const string ChoiceTargets = "ES.SEL.GO";
    private const string ChoiceTexts = "ES.SEL.SET";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().AddCommand(Go, "LABEL").Build();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-scenes-" + Guid.NewGuid().ToString("N"));
    private readonly YuRisEngine _engine = new();

    public YuRisSceneEdgesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static SyntheticYstbBuilder NewScript() => new(Yscm);

    private static SyntheticYslbBuilder Labels() => new SyntheticYslbBuilder()
        .AddLabel("SCENE_B", 2)
        .AddLabel("SCENE_C", 3)
        .AddLabel("SELF_LABEL", 1);

    private IReadOnlyList<SceneEdge> Read(YslbFile? labels, params (int Index, SyntheticYstbBuilder Script)[] scripts)
    {
        var context = new YuRisContext(YscmReader.Read(Yscm.Bytes, "ysc.ybn"), labels: labels);
        var unpacked = new List<UnpackedScript>();
        foreach (var (index, script) in scripts)
        {
            var name = $"yst{index:D5}.ybn";
            var path = Path.Combine(_root, name);
            File.WriteAllBytes(path, script.Build().Bytes);
            unpacked.Add(new UnpackedScript(name, path, _engine.Scripts[0], null));
        }

        return _engine.ReadSceneEdges(new UnpackedGame(context, unpacked));
    }

    private static YslbFile ReadLabels(SyntheticYslbBuilder builder) => YslbReader.Read(builder.Build(), "ysl.ybn");

    private static SyntheticYstbBuilder Empty() => NewScript().AddReturnCode(0, 1);

    [Fact]
    public void Destino_que_nao_se_le_com_seguranca_nao_gera_aresta()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.ResourceArgument([0x48, 0x03, 0x00, 0x24, 0x3E, 0x04]));
        b.AddCommand(Go, 2, b.StringExpression("NAO_EXISTE"));
        b.AddCommand(Gosub, 3, b.StringExpression(string.Empty));
        b.AddCommand(Go, 4);
        b.AddCommand(Gosub, 5);

        Assert.Empty(Read(ReadLabels(Labels()), (1, b), (2, Empty())));
    }

    [Fact]
    public void Script_ilegivel_nao_derruba_a_leitura_dos_outros()
    {
        var good = NewScript();
        good.AddCommand(Go, 1, good.StringExpression("SCENE_B"));
        var badPath = Path.Combine(_root, "yst00005.ybn");
        File.WriteAllBytes(badPath, [1, 2, 3]);
        var context = new YuRisContext(YscmReader.Read(Yscm.Bytes, "ysc.ybn"), labels: ReadLabels(Labels()));
        var goodPath = Path.Combine(_root, "yst00001.ybn");
        File.WriteAllBytes(goodPath, good.Build().Bytes);
        var empty = Path.Combine(_root, "yst00002.ybn");
        File.WriteAllBytes(empty, Empty().Build().Bytes);
        var format = _engine.Scripts[0];

        var edges = _engine.ReadSceneEdges(new UnpackedGame(
            context,
            [
                new UnpackedScript("yst00001.ybn", goodPath, format, null),
                new UnpackedScript("yst00002.ybn", empty, format, null),
                new UnpackedScript("yst00005.ybn", badPath, format, null),
            ]));

        Assert.Single(edges);
    }

    [Fact]
    public void Open_com_ysl_corrompido_avisa_e_o_grafo_fica_vazio()
    {
        File.WriteAllBytes(Path.Combine(_root, "ysc.ybn"), Yscm.Bytes);
        File.WriteAllBytes(Path.Combine(_root, "ysl.ybn"), [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        File.WriteAllBytes(Path.Combine(_root, "yst00001.ybn"), Empty().Build().Bytes);

        var game = _engine.Open(_root);

        Assert.Contains(game.Warnings, w => w.StartsWith("ysl.ybn", StringComparison.Ordinal));
        Assert.Empty(SceneGraph.Read(_engine, game).Edges);
    }

    private sealed class OtherContext() : EngineContext("outra");
}
