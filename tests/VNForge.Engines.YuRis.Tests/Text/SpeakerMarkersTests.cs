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

    private static readonly ITextEncoder Cp1252Guillemets = TextEncoderRegistry.Default.Create("cp1252", SpeakerDelimiterStyle.Guillemets);

    private static readonly ITextEncoder Fold = TextEncoderRegistry.Default.Create("fold");

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
    [InlineData("）", ")")]
    public void Resolve_Cp1252ComGuillemets_TrocaOsDelimitadoresJaponesesPelosDeUmByte(string delimiter, string expected)
    {
        Assert.Equal(expected, SpeakerDelimiters.Resolve(Cp1252Guillemets, delimiter));
    }

    [Fact]
    public void Inject_Cp1252ComOMarcadorJaTrocado_Idempotente()
    {
        var once = Inject(SystemScript().Build().Bytes, Cp1252);

        Assert.Equal(once, Inject(once, Cp1252));
    }

    [Fact]
    public void Inject_Cp1252_OFalanteEscolhidoNoPensamentoUsaOMesmoParentese()
    {
        var b = SystemScript();
        b.AddWord("美咲（疲れたな）", 18);
        b.AddReturnCode(0, 19);
        var bytes = b.Build().Bytes;
        var file = new ScriptFile(ScriptName, bytes);
        var script = Read(bytes);
        var unit = YstbTextExtractor.Extract(file, script, Commands).Units.Single(u => u.Kind == TextUnitKind.Dialogue)
            with { Translation = "(Estou cansada.)", ChosenSpeaker = "Inui", Status = TranslationStatus.Translated };

        var rewritten = Read(YstbTextInjector.Inject(file, script, [unit], Cp1252));

        var marker = Argument(rewritten, 2, 1);
        var message = Argument(rewritten, 4, 0);
        Assert.Equal([.. "Inui(Estou cansada.)"u8], message);
        Assert.Equal(marker[4], message["Inui".Length]);
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
}
