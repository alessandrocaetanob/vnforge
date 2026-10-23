using System.Text;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Tests.Text;

public class YstbTextInjectorTests
{
    private const string ScriptName = "ybin/YST00001.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YscmFile Commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");

    private static readonly ShiftJisTextEncoder Encoder = new();

    private static SyntheticYstbBuilder NewBuilder() => new(Yscm);

    private static string Id(int instruction, int argument = 0) => $"yst00001:{(instruction * 256) + argument:D6}";

    private sealed record Loaded(ScriptFile File, YstbFile Script, ScriptExtraction Extraction);

    private static Loaded Load(byte[] bytes)
    {
        var script = YstbReader.Read(bytes, ScriptName, Commands);
        var file = new ScriptFile(ScriptName, bytes);
        return new Loaded(file, script, YstbTextExtractor.Extract(file, script, Commands));
    }

    private static Loaded Load(SyntheticYstbBuilder builder) => Load(builder.Build().Bytes);

    private static TextUnit Translate(Loaded loaded, string id, string translation) =>
        loaded.Extraction.Units.Single(u => u.Id == id) with { Translation = translation, Status = TranslationStatus.Translated };

    private static byte[] Inject(Loaded loaded, params TextUnit[] units) =>
        YstbTextInjector.Inject(loaded.File, loaded.Script, units, Encoder);

    private static byte[] ArgumentSlice(YstbFile file, int index) => file.ArgumentBytes.Span.Slice(index * 12, 12).ToArray();

    private static void AssertOnlyArgumentsChanged(YstbFile before, YstbFile after, params int[] changed)
    {
        Assert.Equal(before.ArgumentCount, after.ArgumentCount);
        for (var i = 0; i < before.ArgumentCount; i++)
        {
            if (!changed.Contains(i))
            {
                Assert.Equal(ArgumentSlice(before, i), ArgumentSlice(after, i));
            }
        }
    }

    private static SyntheticYstbBuilder FullScript()
    {
        var b = NewBuilder();
        b.AddWord("美咲「今日は、", 1);
        b.AddWord("いい天気だね。」", 2);
        b.AddReturnCode(0, 3);
        b.AddWordBytes([.. GameEncodings.ShiftJis.GetBytes("風が吹いた。"), 0xEF, 0xF2, .. GameEncodings.ShiftJis.GetBytes("次")], 4);
        b.AddReturnCode(0, 5);
        b.AddWord("「ただいま」", 6);
        b.AddReturnCode(0, 7);
        b.AddWord("美咲（疲れたな）", 8);
        b.AddCommand("GOSUB", 9, b.StringExpression("ES.SEL.SET"), b.StringExpression("はい"), b.StringExpression("いいえ"));
        b.AddCommand("GOSUB", 10, b.StringExpression("es.char.name"), b.StringExpression("美咲"));
        b.AddCommand("WAIT", 11, b.StringExpression("一行目\\n二行目\\t三"));
        b.AddCommand("WAIT", 12, b.StringExpression("He said \"あ\"", '`'));
        b.AddCommand("WAIT", 13, b.StringExpression("あ'い", '"'));
        b.AddReturnCode(7, 14);
        b.AddCommand("IF", 15, b.StringExpression("日本語"), new SyntheticYstbArgument(0, 1, 0, 99, 12345), new SyntheticYstbArgument(0, 1, 0, 98, 54321));
        return b;
    }

    [Fact]
    public void Inject_SemTraducoes_GeraArquivoIdentico()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);

        Assert.Equal(built.Bytes, Inject(loaded));
    }

    [Fact]
    public void Inject_UnidadesQueNaoPrecisamDeReescrita_GeramArquivoIdentico()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        var units = loaded.Extraction.Units
            .Select((u, i) => i % 2 == 0
                ? u with { Translation = u.Source, Status = TranslationStatus.Translated }
                : u with { Translation = "ignorada", Status = TranslationStatus.Untranslated })
            .ToArray();

        Assert.Equal(built.Bytes, Inject(loaded, units));
    }

    [Fact]
    public void Inject_UmaLinhaTrocada_OsOutrosArgumentosSaemIdenticosEALinhaNovaELidaDeVolta()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "「ただいま、みんな」");

        var rewritten = Inject(loaded, unit);
        var reloaded = Load(rewritten);

        AssertOnlyArgumentsChanged(loaded.Script, reloaded.Script, loaded.Script.Instructions[5].FirstArgumentIndex);
        Assert.Equal(loaded.Extraction.Units.Count, reloaded.Extraction.Units.Count);
        Assert.Equal(
            loaded.Extraction.Units.Select(u => u.Id == unit.Id ? "「ただいま、みんな」" : u.Source),
            reloaded.Extraction.Units.Select(u => u.Source));
        Assert.Equal(loaded.Extraction.Units.Select(u => u.Id), reloaded.Extraction.Units.Select(u => u.Id));
    }

    [Fact]
    public void Inject_MensagemDeVariosWord_TraducaoInteiraNoPrimeiroEOsOutrosVazios()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(0), "A long line, now in one piece.");

        var reloaded = Load(Inject(loaded, unit));

        var read = reloaded.Extraction.Units.Single(u => u.Id == Id(0));
        Assert.Equal("A long line, now in one piece.", read.Source);
        Assert.Equal(1, read.Locator!.Value.GetProperty("parts").GetArrayLength());
        Assert.Equal(
            GameEncodings.ShiftJis.GetBytes("美咲「A long line, now in one piece.」"),
            reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray());
        Assert.Equal(0u, reloaded.Script.Instructions[1].Arguments[0].Size);
        AssertOnlyArgumentsChanged(loaded.Script, reloaded.Script, 0, 1);
    }

    [Fact]
    public void Inject_Dialogo_VoltaOFalanteEAsAspasJaponesas()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(0), "Hello there.");

        var reloaded = Load(Inject(loaded, unit));

        var read = reloaded.Extraction.Units.Single(u => u.Id == Id(0));
        Assert.Equal(("美咲", "Hello there.", TextUnitKind.Dialogue), (read.Speaker, read.Source, read.Kind));
        var raw = reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray();
        Assert.Equal(GameEncodings.ShiftJis.GetBytes("美咲「Hello there.」"), raw);
    }

    [Fact]
    public void Inject_FalanteComParenteses_MantemOsParentesesDaTraducao()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(7), "（Tired.）");

        var read = Load(Inject(loaded, unit)).Extraction.Units.Single(u => u.Id == Id(7));

        Assert.Equal(("美咲", "（Tired.）"), (read.Speaker, read.Source));
    }

    [Fact]
    public void Inject_FalanteTraduzidoNaUnidade_ValeOdaUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(0), "Hello.") with { Speaker = "Misaki" };

        var raw = Inject(loaded, unit);
        var reloaded = Load(raw);

        Assert.Equal(
            GameEncodings.ShiftJis.GetBytes("Misaki「Hello.」"),
            reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray());
    }

    [Fact]
    public void Inject_MarcadoresDeControle_VoltamACodigosDeControle()
    {
        var loaded = Load(FullScript());
        const string translation = "Wind blew.\\nStill.\\p{EF:F8}Next \\\\ \\{x}";
        var unit = Translate(loaded, Id(3), translation);

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal(translation, reloaded.Extraction.Units.Single(u => u.Id == Id(3)).Source);
        var raw = reloaded.Script.GetResource(reloaded.Script.Instructions[3].Arguments[0]).ToArray();
        Assert.Equal(
            [.. "Wind blew."u8.ToArray(), 0xEF, 0xF0, .. "Still."u8.ToArray(), 0xEF, 0xF2, 0xEF, 0xF8, .. "Next \\ {x}"u8.ToArray()],
            raw);
    }

    [Fact]
    public void Inject_StringDeExpressao_MantemODelimitadorEORotuloDoGosub()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(8, 1), "Yes");

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal(TextUnitKind.Choice, reloaded.Extraction.Units.Single(u => u.Id == Id(8, 1)).Kind);
        Assert.Equal("Yes", reloaded.Extraction.Units.Single(u => u.Id == Id(8, 1)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[8].Arguments[1]).ToArray();
        Assert.Equal([0x4D, 5, 0, (byte)'"', (byte)'Y', (byte)'e', (byte)'s', (byte)'"'], expression);
        AssertOnlyArgumentsChanged(loaded.Script, reloaded.Script, loaded.Script.Instructions[8].FirstArgumentIndex + 1);
    }

    [Fact]
    public void Inject_StringComAspas_UsaCrase()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(10), "He said \"hi\" 訳");

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal("He said \"hi\" 訳", reloaded.Extraction.Units.Single(u => u.Id == Id(10)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[10].Arguments[0]).ToArray();
        Assert.Equal(((byte)'`', (byte)'`'), (expression[3], expression[^1]));
    }

    [Fact]
    public void Inject_ApostrofoNumaStringComAspasDuplas_TrocaODelimitadorPorCrase()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(12), "it's \"x\" 訳");

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal("it's \"x\" 訳", reloaded.Extraction.Units.Single(u => u.Id == Id(12)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[12].Arguments[0]).ToArray();
        Assert.Equal((byte)'`', expression[3]);
    }

    [Fact]
    public void Inject_ApostrofoNumaStringComAspasDuplas_MantemAsAspasQuandoCabe()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(12), "it's 訳");

        var reloaded = Load(Inject(loaded, unit));

        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[12].Arguments[0]).ToArray();
        Assert.Equal((byte)'"', expression[3]);
    }

    [Fact]
    public void Inject_QuebraDeLinhaBarraETabulacao_ViramEscapesDeExpressao()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(10), "A\\nB\\\\C\t訳");

        var reloaded = Load(Inject(loaded, unit));

        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[10].Arguments[0]).ToArray();
        Assert.Equal(GameEncodings.ShiftJis.GetBytes("\"A\\nB\\\\C\\t訳\""), expression[3..]);
        Assert.Equal("A\\nB\\\\C\t訳", reloaded.Extraction.Units.Single(u => u.Id == Id(10)).Source);
    }

    [Fact]
    public void Inject_TextoJaponesComBarraNoSegundoByte_NaoEscapaOByteDeCaractere()
    {
        var builder = NewBuilder();
        builder.AddCommand("WAIT", 1, builder.StringExpression("あ"));
        var loaded = Load(builder);

        var reloaded = Load(Inject(loaded, Translate(loaded, Id(0), "ソース")));

        Assert.Equal("ソース", reloaded.Extraction.Units.Single(u => u.Id == Id(0)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray();
        Assert.Equal(GameEncodings.ShiftJis.GetBytes("\"ソース\""), expression[3..]);
    }

    [Fact]
    public void Inject_AspasDuplasCraseEApostrofoJuntos_FalhaSemTrocarNada()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(10), "a\"b`c'd");

        var error = Assert.Throws<ArgumentException>(() => Inject(loaded, unit));

        Assert.Contains(Id(10), error.Message);
    }

    [Fact]
    public void Inject_TraducaoQuePassaDoLimiteDeUmaStringDeExpressao_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(10), new string('あ', 40000));

        var error = Assert.Throws<ArgumentException>(() => Inject(loaded, unit));

        Assert.Contains("limite", error.Message);
    }

    [Fact]
    public void Inject_CaractereQueOShiftJisNaoRepresenta_FalhaCitandoAUnidade()
    {
        var loaded = Load(FullScript());
        var ok = Translate(loaded, Id(5), "「ただいま、みんな」");
        var bad = Translate(loaded, Id(3), "ação");

        var error = Assert.Throws<EncoderFallbackException>(() => Inject(loaded, ok, bad));

        Assert.Contains(Id(3), error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Inject_MarcadorInvalido_FalhaCitandoAUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "oi \\x");

        var error = Assert.Throws<FormatException>(() => Inject(loaded, unit));

        Assert.Contains(Id(5), error.Message);
    }

    [Fact]
    public void Inject_BytesCodificadosIguaisAosOriginais_CopiaCruSemTrocar()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        var unit = Translate(loaded, Id(5), "qualquer coisa");
        var original = GameEncodings.ShiftJis.GetBytes("「ただいま」");

        var rewritten = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], new FixedEncoder(original));

        Assert.Equal(built.Bytes, rewritten);
    }

    [Fact]
    public void Inject_QualquerOrdemEUmSubconjunto_DaOMesmoResultado()
    {
        var loaded = Load(FullScript());
        var a = Translate(loaded, Id(5), "「やあ」");
        var b = Translate(loaded, Id(9, 1), "Name");
        var c = Translate(loaded, Id(0), "Hi.");

        Assert.Equal(Inject(loaded, a, b, c), Inject(loaded, c, a, b));
        Assert.NotEqual(Inject(loaded, a, b, c), Inject(loaded, a, b));
    }

    [Fact]
    public void Inject_Resultado_NaoAlteraOScriptOriginal()
    {
        var built = FullScript().Build();
        var snapshot = built.Bytes.ToArray();
        var loaded = Load(built.Bytes);

        _ = Inject(loaded, Translate(loaded, Id(5), "「やあ」"));

        Assert.Equal(snapshot, built.Bytes);
        Assert.Equal(snapshot, loaded.File.Content.ToArray());
    }

    [Fact]
    public void Inject_ScriptSemChaveConhecidaTrocandoOutroArgumento_VoltaALer()
    {
        var builder = NewBuilder().WithKey(0x12345678);
        builder.AddCommand("WAIT", 1, builder.StringExpression("OK"));
        builder.AddWord("あ", 2);
        var loaded = Load(builder);

        var reloaded = Load(Inject(loaded, Translate(loaded, Id(1), "A")));

        Assert.Equal(0x12345678u, reloaded.Script.Key);
        Assert.Equal("A", reloaded.Extraction.Units.Single().Source);
    }

    [Fact]
    public void Inject_UnidadeSemLocator_FalhaCitandoAUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "x");
        unit = unit with { Locator = null };

        var error = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains(Id(5), error.Message);
    }

    [Fact]
    public void Inject_LocatorDeOutraVersao_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "x");
        unit = unit with { Locator = Json("""{ "version": 2, "command": "WORD", "parts": [] }""") };

        Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));
    }

    [Theory]
    [InlineData("""{ "version": 1, "command": "WORD" }""")]
    [InlineData("""{ "version": 1, "command": "WORD", "parts": [ { "instruction": "a" } ] }""")]
    [InlineData("""{ "version": 1, "command": "WORD", "parts": [ { "instruction": 999, "argument": 0, "size": 1, "offset": 0 } ] }""")]
    [InlineData("""{ "version": 1, "command": "WORD", "parts": [ { "instruction": 5, "argument": 7, "size": 1, "offset": 0 } ] }""")]
    [InlineData("""{ "version": 1, "command": "GOSUB", "parts": [ { "instruction": 5, "argument": 0, "size": 1, "offset": 0 } ] }""")]
    [InlineData("""{ "version": 1, "command": "WORD", "speaker": { "name": "x" }, "parts": [ { "instruction": 5, "argument": 0, "size": 1, "offset": 0 } ] }""")]
    public void Inject_LocatorMalFormadoOuQueNaoCasaComOScript_Falha(string locator)
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "x") with { Locator = Json(locator) };

        var error = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains(Id(5), error.Message);
    }

    [Fact]
    public void Inject_SizeEOffsetQueMudaramDesdeAExtracao_Falha()
    {
        var loaded = Load(FullScript());
        var other = Load(NewBuilder().AddWord("あ", 1).AddWord("い", 2).AddReturnCode(0, 3).AddWord("「ただいま」", 4).AddWord("う", 5).AddWord("え", 6).AddWord("お", 7));
        var unit = Translate(loaded, Id(5), "x");

        var error = Assert.Throws<InvalidDataException>(() => YstbTextInjector.Inject(loaded.File, other.Script, [unit], Encoder));

        Assert.Contains(Id(5), error.Message);
    }

    [Fact]
    public void Inject_SourceDiferenteDoTextoDoScript_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "x") with { Source = "「ちがう」" };

        var error = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains("original", error.Message);
    }

    [Fact]
    public void Inject_SourceDeStringDeExpressaoDiferenteDoScript_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(10), "x") with { Source = "別の" };

        Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));
    }

    [Fact]
    public void Inject_UnidadeSemTraducaoComLocatorInvalido_EIgnorada()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        var unit = loaded.Extraction.Units[0] with { Locator = null };

        Assert.Equal(built.Bytes, Inject(loaded, unit));
    }

    [Fact]
    public void Inject_IdDeOutroScriptOuRepetido_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, Id(5), "x");

        Assert.Throws<ArgumentException>(() => Inject(loaded, unit with { Id = "yst99999:000001" }));
        Assert.Throws<ArgumentException>(() => Inject(loaded, unit, unit));
    }

    [Fact]
    public void Inject_DuasUnidadesNoMesmoArgumento_Falha()
    {
        var loaded = Load(FullScript());
        var a = Translate(loaded, Id(5), "a");
        var b = a with { Id = Id(5, 1) };

        Assert.Throws<InvalidDataException>(() => Inject(loaded, a, b));
    }

    [Fact]
    public void Inject_ReextrairDepoisDeInjetar_DaOsMesmosIdsComAsTraducoesNoSource()
    {
        var loaded = Load(FullScript());
        var translated = loaded.Extraction.Units
            .Select((u, i) => u with { Translation = u.Source.StartsWith('（') ? $"（訳{i}）" : $"訳{i}", Status = TranslationStatus.Translated })
            .ToArray();
        var expectedSources = translated.Select(u => u.Translation).ToArray();

        var reloaded = Load(Inject(loaded, translated));

        Assert.Equal(loaded.Extraction.Units.Select(u => u.Id), reloaded.Extraction.Units.Select(u => u.Id));
        Assert.Equal(expectedSources, reloaded.Extraction.Units.Select(u => u.Source));
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixedEncoder(byte[] bytes) : ITextEncoder
    {
        public string Id => "fixed";

        public byte[] Encode(string text) => bytes;

        public IReadOnlyList<int> FindUnencodable(string text) => [];
    }
}
