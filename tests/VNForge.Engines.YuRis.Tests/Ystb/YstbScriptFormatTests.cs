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
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YuRisContext Context = new(YscmReader.Read(Yscm.Bytes, "ysc.ybn"));

    private static readonly YstbScriptFormat Format = new();

    private static byte[] Script() =>
        new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290).AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2).Build().Bytes;

    [Fact]
    public void Identifica_o_formato_e_a_engine_expoe_o_formato()
    {
        Assert.Equal("ystb", Format.Id);
        Assert.Equal([".ybn"], Format.FileExtensions);
        Assert.IsType<YstbScriptFormat>(Assert.Single(new YuRisEngine().Scripts));
    }

    [Fact]
    public void Extract_devolve_as_unidades_do_script()
    {
        var extraction = Format.Extract(new ScriptFile("yst00001.ybn", Script()), Context);

        var unit = Assert.Single(extraction.Units);
        Assert.Equal("yst00001:000000", unit.Id);
        Assert.Equal(TextUnitKind.Dialogue, unit.Kind);
        Assert.Equal("美咲", unit.Speaker);
        Assert.Equal("おはよう", unit.Source);
        Assert.NotNull(unit.Locator);
    }

    [Fact]
    public void Extract_de_script_invalido_lanca_InvalidDataException_em_portugues()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Format.Extract(new ScriptFile("yst00001.ybn", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }), Context));

        Assert.Contains("yst00001.ybn", ex.Message);
    }

    [Fact]
    public void Extract_com_contexto_de_outra_engine_lanca_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Format.Extract(new ScriptFile("yst00001.ybn", Script()), new OtherContext()));
    }

    [Fact]
    public void Rebuild_sem_traducao_devolve_o_script_identico()
    {
        var bytes = Script();
        var file = new ScriptFile("yst00001.ybn", bytes);
        var units = Format.Extract(file, Context).Units;

        var rebuilt = Format.Rebuild(file, units, new ShiftJisTextEncoder(), Context);

        Assert.Equal(bytes, rebuilt);
    }

    [Fact]
    public void Rebuild_com_traducao_reextrai_com_o_texto_novo_e_mesmo_id()
    {
        var file = new ScriptFile("yst00001.ybn", Script());
        var original = Format.Extract(file, Context).Units;
        var translated = original.Select(u => u with { Translation = "Good morning", Status = TranslationStatus.Translated }).ToList();

        var rebuilt = Format.Rebuild(file, translated, new ShiftJisTextEncoder(), Context);

        var again = Format.Extract(new ScriptFile("yst00001.ybn", rebuilt), Context).Units;
        var unit = Assert.Single(again);
        Assert.Equal("Good morning", unit.Source);
        Assert.Equal(original[0].Id, unit.Id);
        Assert.Equal("美咲", unit.Speaker);
    }

    [Fact]
    public void Rebuild_de_caractere_fora_do_shift_jis_cita_o_id()
    {
        var file = new ScriptFile("yst00001.ybn", Script());
        var units = Format.Extract(file, Context).Units.Select(u => u with { Translation = "ação 😀", Status = TranslationStatus.Translated }).ToList();

        var ex = Assert.Throws<EncoderFallbackException>(() => Format.Rebuild(file, units, new ShiftJisTextEncoder(), Context));

        Assert.Contains("yst00001:000000", ex.Message);
    }

    [Fact]
    public void Rebuild_com_id_de_outro_script_lanca_ArgumentException()
    {
        var file = new ScriptFile("yst00001.ybn", Script());
        var units = Format.Extract(file, Context).Units.Select(u => u with { Id = "yst00002:000000", Translation = "x", Status = TranslationStatus.Translated }).ToList();

        Assert.Throws<ArgumentException>(() => Format.Rebuild(file, units, new ShiftJisTextEncoder(), Context));
    }

    [Fact]
    public void Rebuild_com_contexto_de_outra_engine_lanca_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Format.Rebuild(new ScriptFile("yst00001.ybn", Script()), [], new ShiftJisTextEncoder(), new OtherContext()));
    }

    private sealed class OtherContext() : EngineContext("outra");
}
