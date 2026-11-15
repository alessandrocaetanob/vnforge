using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Tests.Ystb;

[Trait("Risco", "R3")]
public sealed class YstbDumperTests
{
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static SyntheticYstbBuilder NewBuilder() => new(Yscm);

    private static YstbDump Dump(SyntheticYstbBuilder builder)
    {
        var commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");
        var script = YstbReader.Read(builder.Build().Bytes, "yst00001.ybn", commands);
        return YstbDumper.Dump(script, commands);
    }

    [Fact]
    public void Dump_do_WORD_mostra_o_texto_com_marcadores_de_controle()
    {
        var builder = NewBuilder();
        builder.AddWordBytes([.. GameEncodings.ShiftJis.GetBytes("美咲「やあ"), 0xEF, 0xF0, .. GameEncodings.ShiftJis.GetBytes("またね」"), 0xEF, 0xF2, 0xEF, 0x99], 1);

        var argument = Assert.Single(Assert.Single(Dump(builder).Instructions).Arguments);

        Assert.Equal(YstbArgumentKind.Text, argument.Kind);
        Assert.Equal("TEXT", argument.Name);
        Assert.Equal("美咲「やあ\\nまたね」\\p{EF:99}", argument.Value);
    }
}
