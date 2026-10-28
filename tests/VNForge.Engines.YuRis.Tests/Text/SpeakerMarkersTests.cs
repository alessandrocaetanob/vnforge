using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Tests.Text;

/// <summary>#434: os delimitadores do falante na <c>cp1252</c> e os marcadores <c>es.CHAR.NAME.MARK.SET</c> do script do sistema.</summary>
public class SpeakerMarkersTests
{
    private const string ScriptName = "ybin/YST00073.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YscmFile Commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");

    private static readonly ITextEncoder Cp1252 = TextEncoderRegistry.Default.Create("cp1252");

    private static readonly ShiftJisTextEncoder ShiftJis = new();

    private static readonly YuRisContext Context = new(Commands);

    private static SyntheticYstbBuilder SystemScript()
    {
        var b = new SyntheticYstbBuilder(Yscm);
        b.AddCommand("GOSUB", 14, b.StringExpression("es.CHAR.NAME.MARK.SET"), b.StringExpression("「"));
        b.AddCommand("GOSUB", 15, b.StringExpression("es.CHAR.NAME.MARK.SET"), b.StringExpression("『"));
        b.AddCommand("GOSUB", 16, b.StringExpression("es.CHAR.NAME.MARK.SET"), b.StringExpression("（"));
        b.AddCommand("GOSUB", 17, b.StringExpression("es.CHAR.NAME"), b.StringExpression("真弓"));
        return b;
    }

    private static YstbFile Read(byte[] bytes) => YstbReader.Read(bytes, ScriptName, Commands);

    private static byte[] Argument(YstbFile file, int instruction, int argument) =>
        file.GetResource(file.Instructions[instruction].Arguments[argument]).ToArray();

    private static byte[] Expression(byte quote, params byte[] content) =>
        [0x4D, (byte)(content.Length + 2), 0x00, quote, .. content, quote];

    private static byte[] Inject(byte[] bytes, ITextEncoder encoder) =>
        YstbTextInjector.Inject(new ScriptFile(ScriptName, bytes), Read(bytes), [], encoder);

    [Theory]
    [InlineData("「", "«")]
    [InlineData("『", "«")]
    [InlineData("」", "»")]
    [InlineData("』", "»")]
    public void Resolve_Cp1252_TrocaOsDelimitadoresJaponesesPelosDeUmByte(string delimiter, string expected)
    {
        Assert.Equal(expected, SpeakerDelimiters.Resolve(Cp1252, delimiter));
    }

    [Theory]
    [InlineData("「")]
    [InlineData("』")]
    public void Resolve_ShiftJis_MantemOsDelimitadoresJaponeses(string delimiter)
    {
        Assert.Equal(delimiter, SpeakerDelimiters.Resolve(ShiftJis, delimiter));
    }

    [Theory]
    [InlineData("")]
    [InlineData("（")]
    public void Resolve_SemSubstituto_DevolveOMesmoTexto(string delimiter)
    {
        Assert.Equal(delimiter, SpeakerDelimiters.Resolve(Cp1252, delimiter));
    }

    [Fact]
    public void Cp1252_OsSubstitutosSaoGravaveisEFicamForaDaFaixaDeLeadBytes()
    {
        var bytes = GameEncodings.Windows1252.GetBytes(SpeakerDelimiters.Cp1252Open + SpeakerDelimiters.Cp1252Close);

        Assert.Empty(Cp1252.FindUnencodable(SpeakerDelimiters.Cp1252Open + SpeakerDelimiters.Cp1252Close));
        Assert.All(bytes, b => Assert.False(b is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC)));
    }

    [Fact]
    public void Inject_Cp1252SemUnidades_TrocaOsMarcadoresPorDelimitadoresDeUmByte()
    {
        var bytes = SystemScript().Build().Bytes;

        var rewritten = Read(Inject(bytes, Cp1252));

        Assert.Equal(Expression((byte)'"', 0xAB), Argument(rewritten, 0, 1));
        Assert.Equal(Expression((byte)'"', 0xAB), Argument(rewritten, 1, 1));
    }

    [Fact]
    public void Inject_Cp1252_DeixaIntactoOQueNaoEMarcadorOuNaoTemSubstituto()
    {
        var original = Read(SystemScript().Build().Bytes);

        var rewritten = Read(Inject(SystemScript().Build().Bytes, Cp1252));

        Assert.Equal(Argument(original, 2, 1), Argument(rewritten, 2, 1));
        Assert.Equal(Argument(original, 3, 1), Argument(rewritten, 3, 1));
        Assert.Equal(Argument(original, 0, 0), Argument(rewritten, 0, 0));
        Assert.Equal(original.Instructions.Count, rewritten.Instructions.Count);
    }

    [Fact]
    public void Inject_ShiftJisSemUnidades_GeraArquivoIdentico()
    {
        var bytes = SystemScript().Build().Bytes;

        Assert.Equal(bytes, Inject(bytes, ShiftJis));
    }

    [Fact]
    public void Inject_Cp1252ComOMarcadorJaTrocado_Idempotente()
    {
        var once = Inject(SystemScript().Build().Bytes, Cp1252);

        Assert.Equal(once, Inject(once, Cp1252));
    }

    [Fact]
    public void Inject_Cp1252_OMarcadorBateComODelimitadorDaMensagem()
    {
        var b = SystemScript();
        b.AddWord("美咲「今日は」", 18);
        b.AddReturnCode(0, 19);
        var bytes = b.Build().Bytes;
        var file = new ScriptFile(ScriptName, bytes);
        var script = Read(bytes);
        var unit = YstbTextExtractor.Extract(file, script, Commands).Units.Single(u => u.Kind == TextUnitKind.Dialogue)
            with { Translation = "Bom dia!", Speaker = "Misaki", Status = TranslationStatus.Translated };

        var rewritten = Read(YstbTextInjector.Inject(file, script, [unit], Cp1252));

        var marker = Argument(rewritten, 0, 1);
        var message = Argument(rewritten, 4, 0);
        Assert.Equal(Expression((byte)'"', 0xAB), marker);
        Assert.Equal([.. "Misaki"u8, 0xAB, .. "Bom dia!"u8, 0xBB], message);
        Assert.Equal(0xAB, marker[4]);
        Assert.Equal(marker[4], message["Misaki".Length]);
    }

    [Fact]
    public void Inject_Cp1252ComRotinaEmMaiusculas_TrocaOMarcador()
    {
        var b = new SyntheticYstbBuilder(Yscm);
        b.AddCommand("GOSUB", 1, b.StringExpression("ES.CHAR.NAME.MARK.SET"), b.StringExpression("「"));

        var rewritten = Read(Inject(b.Build().Bytes, Cp1252));

        Assert.Equal(Expression((byte)'"', 0xAB), Argument(rewritten, 0, 1));
    }

    [Fact]
    public void RequiresRebuild_SoQuandoACodificacaoNaoGravaOMarcador()
    {
        var format = new YstbScriptFormat();
        var file = new ScriptFile(ScriptName, SystemScript().Build().Bytes);
        var other = new ScriptFile("ybin/YST00001.ybn", new SyntheticYstbBuilder(Yscm).AddWord("あ", 1).Build().Bytes);

        Assert.True(format.RequiresRebuild(file, Cp1252, Context));
        Assert.False(format.RequiresRebuild(file, ShiftJis, Context));
        Assert.False(format.RequiresRebuild(other, Cp1252, Context));
    }

    [Fact]
    public void Rebuild_Cp1252SemUnidades_TrocaOsMarcadores()
    {
        var format = new YstbScriptFormat();
        var bytes = SystemScript().Build().Bytes;

        var rebuilt = format.Rebuild(new ScriptFile(ScriptName, bytes), [], Cp1252, Context);

        Assert.Equal(Expression((byte)'"', 0xAB), Argument(Read(rebuilt), 0, 1));
    }
}
