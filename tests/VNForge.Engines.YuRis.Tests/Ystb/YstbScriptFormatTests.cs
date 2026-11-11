using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Ystb;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Tests.Ystb;

public class YstbScriptFormatTests
{
    private const string Yst1Ybn = "yst00001.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YuRisContext Context = new(YscmReader.Read(Yscm.Bytes, "ysc.ybn"));

    private static readonly YstbScriptFormat Format = new();

    private static byte[] Script() =>
        new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290).AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2).Build().Bytes;

    [Fact]
    public void Extract_de_script_invalido_lanca_InvalidDataException_em_portugues()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Format.Extract(new ScriptFile(Yst1Ybn, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }), Context));

        Assert.Contains(Yst1Ybn, ex.Message);
    }

    [Fact]
    public void Rebuild_sem_traducao_devolve_o_script_identico()
    {
        var bytes = Script();
        var file = new ScriptFile(Yst1Ybn, bytes);
        var units = Format.Extract(file, Context).Units;

        var rebuilt = Format.Rebuild(file, units, new ShiftJisTextEncoder(), Context);

        Assert.Equal(bytes, rebuilt);
    }

    private sealed class OtherContext() : EngineContext("outra");
}
