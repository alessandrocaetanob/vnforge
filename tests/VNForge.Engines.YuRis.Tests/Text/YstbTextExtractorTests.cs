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

    [Fact]
    public void Extract_CadaTipoSaiComOKindCerto()
    {
        var extraction = Extract(FullScript());

        Assert.Empty(extraction.Warnings);
        Assert.Collection(
            extraction.Units,
            u => AssertUnit(u, Id(0), TextUnitKind.Dialogue, "美咲", "今日は、いい天気だね。"),
            u => AssertUnit(u, Id(3), TextUnitKind.Narration, null, "風が吹いた。\\p次"),
            u => AssertUnit(u, Id(5), TextUnitKind.Dialogue, null, "「ただいま」"),
            u => AssertUnit(u, Id(7), TextUnitKind.Narration, null, "彼は言った。「やあ」"),
            u => AssertUnit(u, Id(9), TextUnitKind.Dialogue, "美咲", "（疲れたな）"),
            u => AssertUnit(u, Id(10, 1), TextUnitKind.Choice, null, "はい"),
            u => AssertUnit(u, Id(10, 2), TextUnitKind.Choice, null, "いいえ"),
            u => AssertUnit(u, Id(11, 1), TextUnitKind.Name, null, "美咲"),
            u => AssertUnit(u, Id(13), TextUnitKind.Other, null, "一行目\\n二行目"),
            u => AssertUnit(u, Id(19), TextUnitKind.Other, null, "He said \"あ\""));
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

    [Fact]
    public void Extract_DoisWordSeguidosFormamUmaMensagem_ESoUmaUnidade()
    {
        var extraction = Extract(NewBuilder().AddWord("一行目", 1).AddWord("二行目", 2).AddWord("三行目", 3));

        var unit = Assert.Single(extraction.Units);
        Assert.Equal("一行目二行目三行目", unit.Source);
        Assert.Equal(Id(0), unit.Id);
        Assert.Equal(3, Property(unit.Locator, PartsKey).GetArrayLength());
    }

    [Theory]
    [InlineData("RETURNCODE")]
    [InlineData("WAIT")]
    [InlineData("_")]
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
    public void Extract_WordVazioNaoGeraUnidadeEMantemAMensagemJunta()
    {
        var extraction = Extract(NewBuilder().AddWord(string.Empty, 1).AddWord("あ", 2).AddWord(string.Empty, 3).AddWord("い", 4).AddWord(string.Empty, 5));

        var unit = Assert.Single(extraction.Units);
        Assert.Equal("あい", unit.Source);
        Assert.Equal(Id(1), unit.Id);
        Assert.Equal(2, Property(unit.Locator, PartsKey).GetArrayLength());
    }

    [Fact]
    public void Extract_ScriptSoComWordVazio_NaoTemUnidades()
    {
        Assert.Empty(Extract(NewBuilder().AddWord(string.Empty, 1)).Units);
    }

    [Fact]
    public void Extract_ScriptSemInstrucoes_NaoTemUnidades()
    {
        Assert.Empty(Extract(NewBuilder()).Units);
    }

    [Fact]
    public void Extract_FalanteDividoEmDoisWord_FechaOColcheteNoUltimo()
    {
        var extraction = Extract(NewBuilder().AddWord("美咲『こんにちは", 1).AddWord("\u3000世界』", 2));

        var unit = Assert.Single(extraction.Units);
        Assert.Equal(TextUnitKind.Dialogue, unit.Kind);
        Assert.Equal("美咲", unit.Speaker);
        Assert.Equal("こんにちは\u3000世界", unit.Source);
    }

    [Theory]
    [InlineData("彼は、「やあ」")]
    [InlineData("彼は言った。「やあ」")]
    [InlineData("彼は叫んだ！「逃げろ」")]
    [InlineData("名前「閉じない")]
    [InlineData("「閉じない")]
    public void Extract_TextoQueNaoTemFormaDeFalaNaoGeraFalante(string text)
    {
        var unit = Assert.Single(Extract(NewBuilder().AddWord(text, 1)).Units);

        Assert.Null(unit.Speaker);
        Assert.Equal(TextUnitKind.Narration, unit.Kind);
        Assert.Equal(text, unit.Source);
    }

    [Fact]
    public void Extract_FalanteDesconhecidoFeitoSoDeInterrogacoesESeparado()
    {
        var unit = Assert.Single(Extract(NewBuilder().AddWord("？？？「誰だ」", 1)).Units);

        Assert.Equal("？？？", unit.Speaker);
        Assert.Equal("誰だ", unit.Source);
    }

    [Fact]
    public void Extract_FalanteComMarcadorNoNomeNaoESeparado()
    {
        var unit = Assert.Single(Extract(NewBuilder().AddWordBytes([.. Sjis("前"), 0xEF, 0xF0, .. Sjis("名前「あ」")], 1)).Units);

        Assert.Null(unit.Speaker);
        Assert.Equal("前\\n名前「あ」", unit.Source);
    }

    [Fact]
    public void Extract_LocatorDeWordTemInstrucoesArgumentosEFalante()
    {
        var script = FullScript().Build();
        var unit = Extract(script.Bytes).Units[0];

        Assert.Equal(1, Property(unit.Locator, "version").GetInt32());
        Assert.Equal("WORD", Property(unit.Locator, "command").GetString());
        var parts = Property(unit.Locator, PartsKey).EnumerateArray().ToArray();
        Assert.Equal(2, parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var argument = script.Instructions[i].Arguments[0];
            Assert.Equal(i, parts[i].GetProperty("instruction").GetInt32());
            Assert.Equal(0, parts[i].GetProperty("argument").GetInt32());
            Assert.Equal(argument.Size, parts[i].GetProperty("size").GetUInt32());
            Assert.Equal(argument.Offset, parts[i].GetProperty("offset").GetUInt32());
        }

        var speaker = Property(unit.Locator, "speaker");
        Assert.Equal("美咲", speaker.GetProperty("name").GetString());
        Assert.Equal("「", speaker.GetProperty("open").GetString());
        Assert.Equal("」", speaker.GetProperty("close").GetString());
    }

    [Fact]
    public void Extract_LocatorDeWordSemFalanteNaoTemSpeaker()
    {
        var unit = Extract(FullScript()).Units[1];

        Assert.False(unit.Locator!.Value.TryGetProperty("speaker", out _));
    }

    [Fact]
    public void Extract_LocatorDePensamentoNaoAcrescentaColchetes()
    {
        var speaker = Property(Extract(FullScript()).Units[4].Locator, "speaker");

        Assert.Equal("美咲", speaker.GetProperty("name").GetString());
        Assert.Equal(string.Empty, speaker.GetProperty("open").GetString());
        Assert.Equal(string.Empty, speaker.GetProperty("close").GetString());
    }

    [Fact]
    public void Extract_LocatorDeGosubTemARotinaEOArgumento()
    {
        var script = FullScript().Build();
        var unit = Extract(script.Bytes).Units[6];

        Assert.Equal(GosubCommand, Property(unit.Locator, "command").GetString());
        Assert.Equal(SelSetCommand, Property(unit.Locator, "routine").GetString());
        var part = Assert.Single(Property(unit.Locator, PartsKey).EnumerateArray());
        var argument = script.Instructions[10].Arguments[2];
        Assert.Equal(10, part.GetProperty("instruction").GetInt32());
        Assert.Equal(2, part.GetProperty("argument").GetInt32());
        Assert.Equal(argument.Size, part.GetProperty("size").GetUInt32());
        Assert.Equal(argument.Offset, part.GetProperty("offset").GetUInt32());
    }

    [Fact]
    public void Extract_LocatorDeOutraStringTemOComandoEOArgumento()
    {
        var unit = Extract(FullScript()).Units[^1];

        Assert.Equal("WAIT", Property(unit.Locator, "command").GetString());
        Assert.False(unit.Locator!.Value.TryGetProperty("routine", out _));
        Assert.Equal(19, Assert.Single(Property(unit.Locator, PartsKey).EnumerateArray()).GetProperty("instruction").GetInt32());
    }

    [Fact]
    public void Extract_OsIdsSaoOsMesmosEmDuasExtracoes()
    {
        var bytes = FullScript().Build().Bytes;

        var first = Extract(bytes).Units;
        var second = Extract(bytes).Units;

        Assert.Equal(first.Select(u => u.Id), second.Select(u => u.Id));
        Assert.Equal(first.Select(TextUnitJson.Serialize), second.Select(TextUnitJson.Serialize));
        Assert.Equal(first.Count, first.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Extract_OIdNaoMudaQuandoOutraUnidadeApareceDepois()
    {
        var shorter = Extract(NewBuilder().AddWord("あ", 1).AddReturnCode(0, 2).AddWord("い", 3)).Units;
        var longer = Extract(NewBuilder().AddWord("あ", 1).AddReturnCode(0, 2).AddWord("い", 3).AddReturnCode(0, 4).AddWord("う", 5)).Units;

        Assert.Equal(shorter.Select(u => u.Id), longer.Take(2).Select(u => u.Id));
    }

    [Fact]
    public void Extract_OsIdsUsamAChaveDoArquivo()
    {
        var bytes = NewBuilder().AddWord("あ", 1).Build().Bytes;
        var script = YstbReader.Read(bytes, "yst00042.ybn", Commands);

        var unit = Assert.Single(YstbTextExtractor.Extract(new ScriptFile("yst00042.ybn", bytes), script, Commands).Units);

        Assert.Equal("yst00042:000000", unit.Id);
    }

    [Fact]
    public void Extract_EscolhasParamNoPrimeiroArgumentoVazio()
    {
        var b = NewBuilder();
        b.AddCommand(GosubCommand, 1, b.StringExpression(SelSetCommand), b.StringExpression("一"), b.StringExpression(string.Empty), b.StringExpression("二"));

        var unit = Assert.Single(Extract(b).Units);

        Assert.Equal("一", unit.Source);
    }

    [Fact]
    public void Extract_EscolhasParamNoPrimeiroArgumentoQueNaoELiteral()
    {
        var b = NewBuilder();
        b.AddCommand(
            GosubCommand,
            1,
            b.StringExpression(SelSetCommand),
            b.StringExpression("一"),
            b.ResourceArgument([0x48, 0x04, 0x00, 0x24, 0x01, 0x00, 0x00], 1, 3),
            b.StringExpression("二"));

        var unit = Assert.Single(Extract(b).Units);

        Assert.Equal("一", unit.Source);
    }

    [Fact]
    public void Extract_GosubSemArgumentosOuComRotinaQueNaoELiteral_NaoGeraUnidades()
    {
        var b = NewBuilder();
        b.AddCommand(GosubCommand, 1);
        b.AddCommand(GosubCommand, 2, b.ResourceArgument([0x48, 0x04, 0x00, 0x24, 0x01, 0x00, 0x00], 0, 3), b.StringExpression("日本語"));

        Assert.Empty(Extract(b).Units);
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

    [Theory]
    [InlineData("\"")]
    [InlineData("'")]
    [InlineData("`")]
    public void Extract_AceitaAsTresAspasDoYuRis(string quote)
    {
        var b = NewBuilder();
        b.AddCommand("WAIT", 1, b.StringExpression("日本語", quote[0]));

        Assert.Equal("日本語", Assert.Single(Extract(b).Units).Source);
    }

    [Fact]
    public void Extract_EscapesDeCDaExpressaoViramMarcadoresOuTexto()
    {
        var b = NewBuilder();
        b.AddCommand("WAIT", 1, b.StringExpression("あ\\\\い\\nう\\tえ{x}"));

        var unit = Assert.Single(Extract(b).Units);

        Assert.Equal("あ\\\\い\\nう\tえ\\{x}", unit.Source);
    }

    [Fact]
    public void Extract_BarraDentroDeCaractereDeDoisBytesNaoEEscape()
    {
        // ソ = 83 5C: o segundo byte é uma barra, mas pertence ao caractere.
        var b = NewBuilder();
        b.AddCommand("WAIT", 1, b.StringExpression("ソ日本"));

        Assert.Equal("ソ日本", Assert.Single(Extract(b).Units).Source);
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

    [Fact]
    public void Extract_ComandosDeFluxoComArgumentosForaDosRecursosNaoQuebram()
    {
        var b = NewBuilder();
        b.AddCommand("ELSE", 1, b.StringExpression("日本語"), new SyntheticYstbArgument(0, 1, 0, 6000, 6000), new SyntheticYstbArgument(0, 1, 0, 7000, 7000));
        b.AddCommand("LOOP", 2, b.StringExpression("日本語"), new SyntheticYstbArgument(0, 1, 0, 5000, 5000));
        b.AddReturnCode(9999, 3, 8888);
        b.AddWord("終", 4);

        var unit = Assert.Single(Extract(b).Units);

        Assert.Equal("終", unit.Source);
    }

    [Fact]
    public void Extract_ReconstroiOTextoComOLocator_IdaEVoltaDosMarcadores()
    {
        var script = FullScript().Build();
        var unit = Extract(script.Bytes).Units[1];

        var raw = Property(unit.Locator, PartsKey).EnumerateArray()
            .SelectMany(p => script.PlainResources.AsSpan((int)p.GetProperty("offset").GetUInt32(), (int)p.GetProperty("size").GetUInt32()).ToArray())
            .ToArray();

        Assert.Equal(raw, ControlMarkers.ToBytes(unit.Source, new ShiftJisTextEncoder()));
    }

    [Fact]
    public void Extract_ArgumentosNulosSaoRecusados()
    {
        var bytes = NewBuilder().AddWord("あ", 1).Build().Bytes;
        var script = YstbReader.Read(bytes, ScriptName, Commands);
        var file = new ScriptFile(ScriptName, bytes);

        Assert.Throws<ArgumentNullException>(() => YstbTextExtractor.Extract(null!, script, Commands));
        Assert.Throws<ArgumentNullException>(() => YstbTextExtractor.Extract(file, null!, Commands));
        Assert.Throws<ArgumentNullException>(() => YstbTextExtractor.Extract(file, script, null!));
    }

    [Fact]
    public void Extract_YscmSemOsComandosDeTexto_FalhaNomeandoOQueFalta()
    {
        var bytes = NewBuilder().AddWord("あ", 1).Build().Bytes;
        var script = YstbReader.Read(bytes, ScriptName, Commands);
        var incomplete = YscmReader.Read(new SyntheticYscmBuilder().AddCommand("WORD", "TEXT").Build().Bytes, "ysc.ybn");

        var ex = Assert.ThrowsAny<VNForge.Core.VNForgeException>(() => YstbTextExtractor.Extract(new ScriptFile(ScriptName, bytes), script, incomplete));

        Assert.Contains(GosubCommand, ex.Message, StringComparison.Ordinal);
    }
}
