using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Tests.Text;

public class YstbTextExtractorTests
{
    private const string PartsKey = "parts";
    private const string SelSetCommand = "ES.SEL.SET";
    private const string GosubCommand = "GOSUB";

    private const string ScriptName = "ybin/YST00001.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YscmFile Commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");

    private static byte[] Sjis(string text) => GameEncodings.ShiftJis.GetBytes(text);

    private static SyntheticYstbBuilder NewBuilder() => new(Yscm);

    private static ScriptExtraction Extract(SyntheticYstbBuilder builder) => Extract(builder.Build().Bytes);

    private static ScriptExtraction Extract(byte[] bytes)
    {
        var script = YstbReader.Read(bytes, ScriptName, Commands);
        return YstbTextExtractor.Extract(new ScriptFile(ScriptName, bytes), script, Commands);
    }

    private static string Id(int instruction, int argument = 0) => $"yst00001:{(instruction * 256) + argument:D6}";

    private static JsonElement Property(JsonElement? locator, string name) => Assert.NotNull(locator).GetProperty(name);

    private static SyntheticYstbBuilder FullScript()
    {
        var b = NewBuilder();
        b.AddWord("美咲「今日は、", 1);
        b.AddWord("いい天気だね。」", 2);
        b.AddReturnCode(0, 3);
        b.AddWordBytes([.. Sjis("風が吹いた。"), 0xEF, 0xF2, .. Sjis("次")], 4);
        b.AddReturnCode(0, 5);
        b.AddWord("「ただいま」", 6);
        b.AddReturnCode(0, 7);
        b.AddWord("彼は言った。「やあ」", 8);
        b.AddReturnCode(0, 9);
        b.AddWord("美咲（疲れたな）", 10);
        b.AddCommand(GosubCommand, 11, b.StringExpression(SelSetCommand), b.StringExpression("はい"), b.StringExpression("いいえ"), b.StringExpression(string.Empty), b.StringExpression("無視"));
        b.AddCommand(GosubCommand, 12, b.StringExpression("es.char.name"), b.StringExpression("美咲"));
        b.AddCommand(GosubCommand, 13, b.StringExpression("ES.BT.GROUP.CHECK"), b.StringExpression("日本語"));
        b.AddCommand("WAIT", 14, b.StringExpression("一行目\\n二行目"));
        b.AddCommand("WAIT", 15, b.StringExpression("OK"));
        b.AddCommand("_", 16, b.StringExpression("日本語"));
        b.AddCommand("CG", 17, b.StringExpression("日本語"));
        b.AddReturnCode(7, 18);
        b.AddCommand("IF", 19, b.StringExpression("日本語"), new SyntheticYstbArgument(0, 1, 0, 99, 12345), new SyntheticYstbArgument(0, 1, 0, 98, 54321));
        b.AddCommand("WAIT", 20, b.StringExpression("He said \"あ\"", '`'));
        return b;
    }

    private static void AssertUnit(TextUnit unit, string id, TextUnitKind kind, string? speaker, string source)
    {
        Assert.Equal(id, unit.Id);
        Assert.Equal(kind, unit.Kind);
        Assert.Equal(speaker, unit.Speaker);
        Assert.Equal(source, unit.Source);
        Assert.Null(unit.Translation);
        Assert.Equal(TranslationStatus.Untranslated, unit.Status);
        Assert.NotNull(unit.Locator);
    }

    [Theory]
    [InlineData("WAIT")]
    public void Extract_OutraInstrucaoEntreDoisWordSeparaAsMensagens(string between)
    {
        var builder = NewBuilder().AddWord("前", 1);
        if (between == "RETURNCODE")
        {
            builder.AddReturnCode(0, 2);
        }
        else
        {
            builder.AddCommand(between, 2, new SyntheticYstbArgument(0, 1, 0, 0, 0));
        }

        var extraction = Extract(builder.AddWord("後", 3));

        Assert.Equal(["前", "後"], extraction.Units.Select(u => u.Source));
        Assert.Equal([Id(0), Id(2)], extraction.Units.Select(u => u.Id));
    }

    [Fact]
    public void Extract_ScriptSoComWordVazio_NaoTemUnidades()
    {
        Assert.Empty(Extract(NewBuilder().AddWord(string.Empty, 1)).Units);
    }

    [Theory]
    [InlineData("彼は叫んだ！「逃げろ」")]
    [InlineData("名前「閉じない")]
    public void Extract_TextoQueNaoTemFormaDeFalaNaoGeraFalante(string text)
    {
        var unit = Assert.Single(Extract(NewBuilder().AddWord(text, 1)).Units);

        Assert.Null(unit.Speaker);
        Assert.Equal(TextUnitKind.Narration, unit.Kind);
        Assert.Equal(text, unit.Source);
    }

    [Fact]
    public void Extract_StringsFeitasDeOutraExpressaoNaoSaoExtraidas()
    {
        var b = NewBuilder();
        var concat = new byte[] { 0x4D, 0x03, 0x00, 0x22, 0x61, 0x22, 0x4D, 0x03, 0x00, 0x22, 0x62, 0x22, 0x2B, 0x00, 0x00 };
        b.AddCommand("WAIT", 1, b.ResourceArgument(concat, 0, 3));
        b.AddCommand("WAIT", 2, b.ResourceArgument([0x4D, 0x02, 0x00, 0x22, 0x61], 0, 3));
        b.AddCommand("WAIT", 3, b.ResourceArgument([0x4D, 0x05, 0x00, .. Sjis("あいう")], 0, 3));
        b.AddCommand("WAIT", 4, b.ResourceArgument([0x4D, 0x03, 0x00, 0x22, 0x61, 0x27], 0, 3));

        Assert.Empty(Extract(b).Units);
    }

    [Fact]
    public void Extract_EscapeDesconhecidoNaoExtraiEAvisa()
    {
        var b = NewBuilder();
        b.AddCommand("WAIT", 1, b.StringExpression("日本語\\q"));
        b.AddCommand(GosubCommand, 2, b.StringExpression(SelSetCommand), b.StringExpression("選択\\"), b.StringExpression("次"));

        var extraction = Extract(b);

        Assert.Empty(extraction.Units);
        Assert.Equal(2, extraction.Warnings.Count);
        Assert.Contains("instrução 0", extraction.Warnings[0], StringComparison.Ordinal);
        Assert.Contains("instrução 1", extraction.Warnings[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_WordComMaisDeUmArgumentoAvisaEQuebraAMensagem()
    {
        var b = NewBuilder();
        b.AddWord("前", 1);
        b.AddCommand("WORD", 2, b.ResourceArgument(Sjis("あ")), b.ResourceArgument(Sjis("い")));
        b.AddWord("後", 3);

        var extraction = Extract(b);

        Assert.Equal(["前", "後"], extraction.Units.Select(u => u.Source));
        var warning = Assert.Single(extraction.Warnings);
        Assert.Contains("instrução 1", warning, StringComparison.Ordinal);
    }
}
