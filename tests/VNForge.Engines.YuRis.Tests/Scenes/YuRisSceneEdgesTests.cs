using VNForge.Core.Engines;
using VNForge.Core.Scenes;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Yslb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Yslb;

namespace VNForge.Engines.YuRis.Tests.Scenes;

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
    public void Go_com_rotulo_literal_e_um_salto_para_o_script_do_rotulo()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.StringExpression("SCENE_B"));

        var edges = Read(ReadLabels(Labels()), (1, b), (2, Empty()));

        Assert.Equal(new SceneEdge("yst00001", "yst00002", SceneEdgeKind.Jump, "SCENE_B"), Assert.Single(edges));
    }

    [Fact]
    public void Gosub_com_rotulo_literal_e_uma_chamada()
    {
        var b = NewScript();
        b.AddCommand(Gosub, 1, b.StringExpression("SCENE_C"), b.StringExpression("argumento"));

        var edges = Read(ReadLabels(Labels()), (1, b), (3, Empty()));

        Assert.Equal(new SceneEdge("yst00001", "yst00003", SceneEdgeKind.Call, "SCENE_C"), Assert.Single(edges));
    }

    [Fact]
    public void Salto_para_rotulo_do_proprio_script_volta_como_aresta_para_si_mesmo()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.StringExpression("SELF_LABEL"));

        var edges = Read(ReadLabels(Labels()), (1, b));

        Assert.Equal("yst00001", Assert.Single(edges).To);
    }

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
    public void Rotulo_de_script_que_nao_esta_no_jogo_nao_gera_aresta()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.StringExpression("SCENE_B"));

        Assert.Empty(Read(ReadLabels(Labels()), (1, b)));
    }

    [Fact]
    public void Sem_ysl_nao_ha_arestas()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.StringExpression("SCENE_B"));

        Assert.Empty(Read(null, (1, b), (2, Empty())));
    }

    [Fact]
    public void Rotulo_declarado_em_dois_scripts_nao_gera_aresta()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.StringExpression("Ambiguo"));
        var labels = ReadLabels(new SyntheticYslbBuilder().AddLabel("AMBIGUO", 2).AddLabel("ambiguo", 3));

        Assert.Empty(Read(labels, (1, b), (2, Empty()), (3, Empty())));
    }

    [Fact]
    public void Es_sel_go_liga_cada_escolha_ao_texto_do_es_sel_set_seguinte()
    {
        var b = NewScript();
        b.AddCommand(Gosub, 1, b.StringExpression(ChoiceTargets), b.StringExpression("SCENE_B"), b.StringExpression("SCENE_C"), b.StringExpression(string.Empty));
        b.AddCommand("WAIT", 2, b.StringExpression("x"));
        b.AddCommand(Gosub, 3, b.StringExpression(ChoiceTexts), b.StringExpression("はい"), b.StringExpression("いいえ"), b.StringExpression(string.Empty));

        var edges = Read(ReadLabels(Labels()), (1, b), (2, Empty()), (3, Empty()));

        var choices = edges.Where(e => e.Kind == SceneEdgeKind.Choice).ToList();
        Assert.Equal(2, choices.Count);
        Assert.Equal(new SceneEdge("yst00001", "yst00002", SceneEdgeKind.Choice, "SCENE_B") { ChoiceUnitId = "yst00001:000513" }, choices[0]);
        Assert.Equal(new SceneEdge("yst00001", "yst00003", SceneEdgeKind.Choice, "SCENE_C") { ChoiceUnitId = "yst00001:000514" }, choices[1]);
    }

    [Fact]
    public void Es_sel_go_sem_es_sel_set_depois_deixa_o_texto_sem_unidade()
    {
        var b = NewScript();
        b.AddCommand(Gosub, 1, b.StringExpression(ChoiceTargets), b.StringExpression("SCENE_B"));

        var choice = Assert.Single(Read(ReadLabels(Labels()), (1, b), (2, Empty())), e => e.Kind == SceneEdgeKind.Choice);

        Assert.Null(choice.ChoiceUnitId);
    }

    [Fact]
    public void Es_sel_go_so_olha_o_es_sel_set_antes_do_proximo_es_sel_go()
    {
        var b = NewScript();
        b.AddCommand(Gosub, 1, b.StringExpression(ChoiceTargets), b.StringExpression("SCENE_B"));
        b.AddCommand(Gosub, 2, b.StringExpression(ChoiceTargets), b.StringExpression("SCENE_C"));
        b.AddCommand(Gosub, 3, b.StringExpression(ChoiceTexts), b.StringExpression("いいえ"));

        var choices = Read(ReadLabels(Labels()), (1, b), (2, Empty()), (3, Empty())).Where(e => e.Kind == SceneEdgeKind.Choice).ToList();

        Assert.Null(choices.Single(e => e.To == "yst00002").ChoiceUnitId);
        Assert.Equal("yst00001:000513", choices.Single(e => e.To == "yst00003").ChoiceUnitId);
    }

    [Fact]
    public void Texto_depois_de_um_texto_vazio_nao_tem_unidade()
    {
        var b = NewScript();
        b.AddCommand(Gosub, 1, b.StringExpression(ChoiceTargets), b.StringExpression("SCENE_B"), b.StringExpression("SCENE_C"));
        b.AddCommand(Gosub, 2, b.StringExpression(ChoiceTexts), b.StringExpression(string.Empty), b.StringExpression("いいえ"));

        var choices = Read(ReadLabels(Labels()), (1, b), (2, Empty()), (3, Empty())).Where(e => e.Kind == SceneEdgeKind.Choice).ToList();

        Assert.All(choices, e => Assert.Null(e.ChoiceUnitId));
    }

    [Fact]
    public void Es_sel_go_tambem_e_uma_chamada_para_o_script_da_rotina()
    {
        var b = NewScript();
        b.AddCommand(Gosub, 1, b.StringExpression(ChoiceTargets), b.StringExpression("SCENE_B"));
        var labels = ReadLabels(Labels().AddLabel(ChoiceTargets, 4));

        var edges = Read(labels, (1, b), (2, Empty()), (4, Empty()));

        Assert.Contains(new SceneEdge("yst00001", "yst00004", SceneEdgeKind.Call, ChoiceTargets), edges);
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
    public void Contexto_de_outra_engine_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => _engine.ReadSceneEdges(new UnpackedGame(new OtherContext(), [])));
    }

    [Fact]
    public void Open_le_o_ysl_da_pasta_descompactada()
    {
        var b = NewScript();
        b.AddCommand(Go, 1, b.StringExpression("SCENE_B"));
        File.WriteAllBytes(Path.Combine(_root, "ysc.ybn"), Yscm.Bytes);
        File.WriteAllBytes(Path.Combine(_root, "ysl.ybn"), Labels().Build());
        File.WriteAllBytes(Path.Combine(_root, "yst00001.ybn"), b.Build().Bytes);
        File.WriteAllBytes(Path.Combine(_root, "yst00002.ybn"), Empty().Build().Bytes);

        var game = _engine.Open(_root);
        var graph = SceneGraph.Read(_engine, game);

        Assert.Equal(new SceneEdge("yst00001", "yst00002", SceneEdgeKind.Jump, "SCENE_B"), Assert.Single(graph.Edges));
    }

    [Fact]
    public void Open_sem_ysl_funciona_e_o_grafo_fica_vazio()
    {
        File.WriteAllBytes(Path.Combine(_root, "ysc.ybn"), Yscm.Bytes);
        File.WriteAllBytes(Path.Combine(_root, "yst00001.ybn"), Empty().Build().Bytes);

        var game = _engine.Open(_root);

        Assert.Empty(game.Warnings);
        Assert.Empty(SceneGraph.Read(_engine, game).Edges);
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
