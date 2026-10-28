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

    private static string UnitId(int instruction, int argument = 0) => $"yst00001:{(instruction * 256) + argument:D6}";

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
        var unit = Translate(loaded, UnitId(5), "「ただいま、みんな」");

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
        var unit = Translate(loaded, UnitId(0), "A long line, now in one piece.");

        var reloaded = Load(Inject(loaded, unit));

        var read = reloaded.Extraction.Units.Single(u => u.Id == UnitId(0));
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
        var unit = Translate(loaded, UnitId(0), "Hello there.");

        var reloaded = Load(Inject(loaded, unit));

        var read = reloaded.Extraction.Units.Single(u => u.Id == UnitId(0));
        Assert.Equal(("美咲", "Hello there.", TextUnitKind.Dialogue), (read.Speaker, read.Source, read.Kind));
        var raw = reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray();
        Assert.Equal(GameEncodings.ShiftJis.GetBytes("美咲「Hello there.」"), raw);
    }

    [Fact]
    public void Inject_FalanteComParenteses_MantemOsParentesesDaTraducao()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(7), "（Tired.）");

        var read = Load(Inject(loaded, unit)).Extraction.Units.Single(u => u.Id == UnitId(7));

        Assert.Equal(("美咲", "（Tired.）"), (read.Speaker, read.Source));
    }

    private static readonly ITextEncoder Cp1252 = TextEncoderRegistry.Default.Create("cp1252");

    private static byte[] FirstMessage(byte[] raw)
    {
        var reloaded = YstbReader.Read(raw, ScriptName, Commands);
        return reloaded.GetResource(reloaded.Instructions[0].Arguments[0]).ToArray();
    }

    [Fact]
    public void Inject_Cp1252ComFalanteTraduzido_GravaOsDelimitadoresDeUmByteEmVezDoShiftJis()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Bom dia!") with { Speaker = "Inui" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        byte[] expected = [.. "Inui"u8, 0xAB, .. "Bom dia!"u8, 0xBB];
        Assert.Equal(expected, FirstMessage(raw));
    }

    [Fact]
    public void Inject_Cp1252ComFalanteEmAspasDuplasJaponesas_GravaOsMesmosDelimitadores()
    {
        var loaded = Load(NewBuilder().AddWord("美咲『こんにちは』", 1).AddReturnCode(0, 2));
        var unit = Translate(loaded, UnitId(0), "Oi") with { Speaker = "Inui" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        byte[] expected = [.. "Inui"u8, 0xAB, .. "Oi"u8, 0xBB];
        Assert.Equal(expected, FirstMessage(raw));
    }

    [Fact]
    public void Inject_Cp1252ComFalanteESemTraducao_IdaEVoltaIdentica()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        var untouched = loaded.Extraction.Units.Select(u => u with { Translation = null, Status = TranslationStatus.Untranslated }).ToArray();

        Assert.Equal(built.Bytes, YstbTextInjector.Inject(loaded.File, loaded.Script, untouched, Cp1252));
    }

    [Fact]
    public void Inject_Cp1252ComFalanteDeSinaisLargosNormalizado_GravaOsSinaisComuns()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Quem?") with { Speaker = "???" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        Assert.Equal([.. "???"u8, 0xAB, .. "Quem?"u8, 0xBB], FirstMessage(raw));
    }

    [Fact]
    public void Inject_Cp1252ComFalanteAcentuado_GravaOsAcentos()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Olá, ação!") with { Speaker = "Teste ç ã é ô" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        Assert.Equal([.. GameEncodings.Windows1252.GetBytes("Teste ç ã é ô"), 0xAB, .. GameEncodings.Windows1252.GetBytes("Olá, ação!"), 0xBB], FirstMessage(raw));
    }

    [Fact]
    public void Inject_Cp1252ComQuebraDeLinhaDepoisDeAcento_GravaUmEspacoAntesDoControle()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "do que já\\nera...?\\pque até\\nhá") with { Speaker = "Inui" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        byte[] expected = [.. "Inui"u8, 0xAB, .. "do que j"u8, 0xE1, 0x20, 0xEF, 0xF0, .. "era...?"u8, 0xEF, 0xF2, .. "que at"u8, 0xE9, 0x20, 0xEF, 0xF0, .. "h"u8, 0xE1, 0xBB];
        Assert.Equal(expected, FirstMessage(raw));
    }

    [Fact]
    public void Inject_ShiftJisComQuebraDeLinha_NaoGravaEspacoExtra()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "ate\\nha") with { Speaker = "Inui" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Encoder);

        Assert.Equal([.. GameEncodings.ShiftJis.GetBytes("Inui「ate"), 0xEF, 0xF0, .. "ha"u8, .. GameEncodings.ShiftJis.GetBytes("」")], FirstMessage(raw));
    }

    [Fact]
    public void Inject_ShiftJisComFalante_MantemOsColchetesJaponeses()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Hello.") with { Speaker = "Misaki" };

        Assert.Equal(GameEncodings.ShiftJis.GetBytes("Misaki「Hello.」"), FirstMessage(Inject(loaded, unit)));
    }

    [Fact]
    public void Inject_Cp1252ComNomeJaponesDoScript_LancaCitandoOFalanteEAUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Bom dia!") with { Speaker = null };

        var ex = Assert.Throws<EncoderFallbackException>(() => YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252));

        Assert.Contains(UnitId(0), ex.Message, StringComparison.Ordinal);
        Assert.Contains("美咲", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'美' (U+7F8E)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Glossário > Personagens", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_Cp1252ComTraducaoForaDaCodificacao_CitaOCaractereETrecho()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Bom dia, 犬!") with { Speaker = "Inui" };

        var ex = Assert.Throws<EncoderFallbackException>(() => YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252));

        Assert.Contains(UnitId(0), ex.Message, StringComparison.Ordinal);
        Assert.Contains("'犬' (U+72AC)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Bom dia, 犬!", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_Cp1252ComFalanteESemTraducao_CopiaOsBytesCrus()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        Assert.Contains(loaded.Extraction.Units, u => u.Speaker is not null);

        Assert.Equal(built.Bytes, YstbTextInjector.Inject(loaded.File, loaded.Script, loaded.Extraction.Units, Cp1252));
    }

    [Fact]
    public void Inject_FalanteTraduzidoNaUnidade_ValeOdaUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Hello.") with { Speaker = "Misaki" };

        var raw = Inject(loaded, unit);
        var reloaded = Load(raw);

        Assert.Equal(
            GameEncodings.ShiftJis.GetBytes("Misaki「Hello.」"),
            reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray());
    }

    private static byte[] MessageAt(byte[] raw, int instruction)
    {
        var reloaded = YstbReader.Read(raw, ScriptName, Commands);
        return reloaded.GetResource(reloaded.Instructions[instruction].Arguments[0]).ToArray();
    }

    [Fact]
    public void Inject_FalanteEscolhido_TrocaOnomeEMantemOsDelimitadoresDoJogo()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Hello.") with { ChosenSpeaker = "Misaki-chan" };

        var raw = Inject(loaded, unit);

        Assert.Equal(GameEncodings.ShiftJis.GetBytes("Misaki-chan「Hello.」"), FirstMessage(raw));
        var read = Load(raw).Extraction.Units.Single(u => u.Id == UnitId(0));
        Assert.Equal(("Misaki-chan", "Hello.", TextUnitKind.Dialogue), (read.Speaker, read.Source, read.Kind));
    }

    [Fact]
    public void Inject_FalanteEscolhido_ValeSobreOFalanteResolvidoDaUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Hello.") with { Speaker = "Outro", ChosenSpeaker = "Misaki" };

        Assert.Equal(GameEncodings.ShiftJis.GetBytes("Misaki「Hello.」"), FirstMessage(Inject(loaded, unit)));
    }

    [Fact]
    public void Inject_Cp1252ComFalanteEscolhido_GravaOsDelimitadoresDaCp1252()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Bom dia!") with { ChosenSpeaker = "Inui" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        Assert.Equal([.. "Inui"u8, 0xAB, .. "Bom dia!"u8, 0xBB], FirstMessage(raw));
    }

    [Fact]
    public void Inject_FalanteEscolhidoComoNarracao_TiraSoONomeEMantemAsAspas()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Hello.") with { ChosenSpeaker = string.Empty };

        var raw = Inject(loaded, unit);

        Assert.Equal(GameEncodings.ShiftJis.GetBytes("「Hello.」"), FirstMessage(raw));
        var read = Load(raw).Extraction.Units.Single(u => u.Id == UnitId(0));
        Assert.Equal((null, "「Hello.」", TextUnitKind.Dialogue), (read.Speaker, read.Source, read.Kind));
    }

    [Fact]
    public void Inject_Cp1252NarracaoEscolhidaNumFalante_TiraOnomeEMantemOParDaCp1252()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Bom dia!") with { ChosenSpeaker = string.Empty };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        Assert.Equal([0xAB, .. "Bom dia!"u8, 0xBB], FirstMessage(raw));
    }

    [Fact]
    public void Inject_NarracaoEscolhidaNumFalanteComParenteses_DeixaOsParenteses()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(7), "（Tired.）") with { ChosenSpeaker = string.Empty };

        var read = Load(Inject(loaded, unit)).Extraction.Units.Single(u => u.Id == UnitId(7));

        Assert.Equal((null, "（Tired.）"), (read.Speaker, read.Source));
    }

    [Fact]
    public void Inject_FalanteEscolhidoNumaNarracao_MontaNomeEAspasJaponesas()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(3), "The wind blew.") with { ChosenSpeaker = "Misaki" };

        var raw = Inject(loaded, unit);

        Assert.Equal(GameEncodings.ShiftJis.GetBytes("Misaki「The wind blew.」"), MessageAt(raw, 3));
        var read = Load(raw).Extraction.Units.Single(u => u.Id == UnitId(3));
        Assert.Equal(("Misaki", "The wind blew.", TextUnitKind.Dialogue), (read.Speaker, read.Source, read.Kind));
    }

    [Fact]
    public void Inject_FalanteEscolhidoNumaFalaJaEntreAspasSemNome_PoeSoONome()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(5), "「Hi, all」") with { ChosenSpeaker = "Misaki" };

        Assert.Equal(GameEncodings.ShiftJis.GetBytes("Misaki「Hi, all」"), MessageAt(Inject(loaded, unit), 5));
    }

    [Fact]
    public void Inject_Cp1252FalanteEscolhidoNumaNarracao_GravaOsDelimitadoresDaCp1252()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(3), "Vento.") with { ChosenSpeaker = "Inui" };

        var raw = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252);

        Assert.Equal([.. "Inui"u8, 0xAB, .. "Vento."u8, 0xBB], MessageAt(raw, 3));
    }

    [Fact]
    public void Inject_NarracaoEscolhidaNumaLinhaSemFalante_NaoMudaNada()
    {
        var loaded = Load(FullScript());
        var plain = Translate(loaded, UnitId(3), "The wind blew.");

        Assert.Equal(Inject(loaded, plain), Inject(loaded, plain with { ChosenSpeaker = string.Empty }));
    }

    [Fact]
    public void Inject_FalanteEscolhidoSemTraducao_IdaEVoltaIdentica()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        var units = loaded.Extraction.Units.Select((u, i) => u with { ChosenSpeaker = i % 2 == 0 ? "Misaki" : string.Empty }).ToArray();

        Assert.Equal(built.Bytes, Inject(loaded, units));
        Assert.Equal(built.Bytes, YstbTextInjector.Inject(loaded.File, loaded.Script, units, Cp1252));
    }

    [Fact]
    public void Inject_FalanteEscolhidoForaDaCodificacao_LancaCitandoAUnidadeEONome()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(0), "Bom dia!") with { ChosenSpeaker = "犬井" };

        var ex = Assert.Throws<EncoderFallbackException>(() => YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], Cp1252));

        Assert.Contains(UnitId(0), ex.Message, StringComparison.Ordinal);
        Assert.Contains("犬井", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_FalanteEscolhidoEmStringDeExpressao_LancaDizendoQueSoMensagemTemFalante()
    {
        var loaded = Load(FullScript());
        var unit = loaded.Extraction.Units.First(u => u.Locator!.Value.GetProperty("command").GetString() != "WORD") with
        {
            Translation = "Yes",
            Status = TranslationStatus.Translated,
            ChosenSpeaker = "Misaki",
        };

        var ex = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains(unit.Id, ex.Message, StringComparison.Ordinal);
        Assert.Contains("falante", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_MarcadoresDeControle_VoltamACodigosDeControle()
    {
        var loaded = Load(FullScript());
        const string translation = "Wind blew.\\nStill.\\p{EF:F8}Next \\\\ \\{x}";
        var unit = Translate(loaded, UnitId(3), translation);

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal(translation, reloaded.Extraction.Units.Single(u => u.Id == UnitId(3)).Source);
        var raw = reloaded.Script.GetResource(reloaded.Script.Instructions[3].Arguments[0]).ToArray();
        Assert.Equal(
            [.. "Wind blew."u8.ToArray(), 0xEF, 0xF0, .. "Still."u8.ToArray(), 0xEF, 0xF2, 0xEF, 0xF8, .. "Next \\ {x}"u8.ToArray()],
            raw);
    }

    [Fact]
    public void Inject_StringDeExpressao_MantemODelimitadorEORotuloDoGosub()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(8, 1), "Yes");

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal(TextUnitKind.Choice, reloaded.Extraction.Units.Single(u => u.Id == UnitId(8, 1)).Kind);
        Assert.Equal("Yes", reloaded.Extraction.Units.Single(u => u.Id == UnitId(8, 1)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[8].Arguments[1]).ToArray();
        Assert.Equal([0x4D, 5, 0, (byte)'"', (byte)'Y', (byte)'e', (byte)'s', (byte)'"'], expression);
        AssertOnlyArgumentsChanged(loaded.Script, reloaded.Script, loaded.Script.Instructions[8].FirstArgumentIndex + 1);
    }

    [Fact]
    public void Inject_StringComAspas_UsaCrase()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(10), "He said \"hi\" 訳");

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal("He said \"hi\" 訳", reloaded.Extraction.Units.Single(u => u.Id == UnitId(10)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[10].Arguments[0]).ToArray();
        Assert.Equal(((byte)'`', (byte)'`'), (expression[3], expression[^1]));
    }

    [Fact]
    public void Inject_ApostrofoNumaStringComAspasDuplas_TrocaODelimitadorPorCrase()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(12), "it's \"x\" 訳");

        var reloaded = Load(Inject(loaded, unit));

        Assert.Equal("it's \"x\" 訳", reloaded.Extraction.Units.Single(u => u.Id == UnitId(12)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[12].Arguments[0]).ToArray();
        Assert.Equal((byte)'`', expression[3]);
    }

    [Fact]
    public void Inject_ApostrofoNumaStringComAspasDuplas_MantemAsAspasQuandoCabe()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(12), "it's 訳");

        var reloaded = Load(Inject(loaded, unit));

        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[12].Arguments[0]).ToArray();
        Assert.Equal((byte)'"', expression[3]);
    }

    [Fact]
    public void Inject_QuebraDeLinhaBarraETabulacao_ViramEscapesDeExpressao()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(10), "A\\nB\\\\C\t訳");

        var reloaded = Load(Inject(loaded, unit));

        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[10].Arguments[0]).ToArray();
        Assert.Equal(GameEncodings.ShiftJis.GetBytes("\"A\\nB\\\\C\\t訳\""), expression[3..]);
        Assert.Equal("A\\nB\\\\C\t訳", reloaded.Extraction.Units.Single(u => u.Id == UnitId(10)).Source);
    }

    [Fact]
    public void Inject_TextoJaponesComBarraNoSegundoByte_NaoEscapaOByteDeCaractere()
    {
        var builder = NewBuilder();
        builder.AddCommand("WAIT", 1, builder.StringExpression("あ"));
        var loaded = Load(builder);

        var reloaded = Load(Inject(loaded, Translate(loaded, UnitId(0), "ソース")));

        Assert.Equal("ソース", reloaded.Extraction.Units.Single(u => u.Id == UnitId(0)).Source);
        var expression = reloaded.Script.GetResource(reloaded.Script.Instructions[0].Arguments[0]).ToArray();
        Assert.Equal(GameEncodings.ShiftJis.GetBytes("\"ソース\""), expression[3..]);
    }

    [Fact]
    public void Inject_AspasDuplasCraseEApostrofoJuntos_FalhaSemTrocarNada()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(10), "a\"b`c'd");

        var error = Assert.Throws<ArgumentException>(() => Inject(loaded, unit));

        Assert.Contains(UnitId(10), error.Message);
    }

    [Fact]
    public void Inject_TraducaoQuePassaDoLimiteDeUmaStringDeExpressao_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(10), new string('あ', 40000));

        var error = Assert.Throws<ArgumentException>(() => Inject(loaded, unit));

        Assert.Contains("limite", error.Message);
    }

    [Fact]
    public void Inject_CaractereQueOShiftJisNaoRepresenta_FalhaCitandoAUnidade()
    {
        var loaded = Load(FullScript());
        var ok = Translate(loaded, UnitId(5), "「ただいま、みんな」");
        var bad = Translate(loaded, UnitId(3), "ação");

        var error = Assert.Throws<EncoderFallbackException>(() => Inject(loaded, ok, bad));

        Assert.Contains(UnitId(3), error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Inject_MarcadorInvalido_FalhaCitandoAUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(5), "oi \\x");

        var error = Assert.Throws<FormatException>(() => Inject(loaded, unit));

        Assert.Contains(UnitId(5), error.Message);
    }

    [Fact]
    public void Inject_BytesCodificadosIguaisAosOriginais_CopiaCruSemTrocar()
    {
        var built = FullScript().Build();
        var loaded = Load(built.Bytes);
        var unit = Translate(loaded, UnitId(5), "qualquer coisa");
        var original = GameEncodings.ShiftJis.GetBytes("「ただいま」");

        var rewritten = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], new FixedEncoder(original));

        Assert.Equal(built.Bytes, rewritten);
    }

    [Fact]
    public void Inject_ExpressaoOriginalComODelimitadorNoMeio_ComOsMesmosBytes_FicaIntacta()
    {
        var inner = GameEncodings.ShiftJis.GetBytes("あ\"い");
        byte[] expression = [0x4D, (byte)(inner.Length + 2), 0, (byte)'"', .. inner, (byte)'"'];
        var builder = NewBuilder();
        builder.AddCommand("GOSUB", 1, builder.StringExpression("ES.SEL.SET"), builder.ResourceArgument(expression));
        var built = builder.Build();
        var loaded = Load(built.Bytes);
        var unit = Translate(loaded, UnitId(0, 1), "qualquer coisa");

        var rewritten = YstbTextInjector.Inject(loaded.File, loaded.Script, [unit], new FixedEncoder(inner));

        Assert.Equal(built.Bytes, rewritten);
    }

    [Fact]
    public void Inject_QualquerOrdemEUmSubconjunto_DaOMesmoResultado()
    {
        var loaded = Load(FullScript());
        var a = Translate(loaded, UnitId(5), "「やあ」");
        var b = Translate(loaded, UnitId(9, 1), "Name");
        var c = Translate(loaded, UnitId(0), "Hi.");

        Assert.Equal(Inject(loaded, a, b, c), Inject(loaded, c, a, b));
        Assert.NotEqual(Inject(loaded, a, b, c), Inject(loaded, a, b));
    }

    [Fact]
    public void Inject_Resultado_NaoAlteraOScriptOriginal()
    {
        var built = FullScript().Build();
        var snapshot = built.Bytes.ToArray();
        var loaded = Load(built.Bytes);

        _ = Inject(loaded, Translate(loaded, UnitId(5), "「やあ」"));

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

        var reloaded = Load(Inject(loaded, Translate(loaded, UnitId(1), "A")));

        Assert.Equal(0x12345678u, reloaded.Script.Key);
        Assert.Equal("A", reloaded.Extraction.Units.Single().Source);
    }

    [Fact]
    public void Inject_UnidadeSemLocator_FalhaCitandoAUnidade()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(5), "x");
        unit = unit with { Locator = null };

        var error = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains(UnitId(5), error.Message);
    }

    [Fact]
    public void Inject_LocatorDeOutraVersao_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(5), "x");
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
        var unit = Translate(loaded, UnitId(5), "x") with { Locator = Json(locator) };

        var error = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains(UnitId(5), error.Message);
    }

    [Fact]
    public void Inject_SizeEOffsetQueMudaramDesdeAExtracao_Falha()
    {
        var loaded = Load(FullScript());
        var other = Load(NewBuilder().AddWord("あ", 1).AddWord("い", 2).AddReturnCode(0, 3).AddWord("「ただいま」", 4).AddWord("う", 5).AddWord("え", 6).AddWord("お", 7));
        var unit = Translate(loaded, UnitId(5), "x");

        var error = Assert.Throws<InvalidDataException>(() => YstbTextInjector.Inject(loaded.File, other.Script, [unit], Encoder));

        Assert.Contains(UnitId(5), error.Message);
    }

    [Fact]
    public void Inject_SourceDiferenteDoTextoDoScript_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(5), "x") with { Source = "「ちがう」" };

        var error = Assert.Throws<InvalidDataException>(() => Inject(loaded, unit));

        Assert.Contains("original", error.Message);
    }

    [Fact]
    public void Inject_SourceDeStringDeExpressaoDiferenteDoScript_Falha()
    {
        var loaded = Load(FullScript());
        var unit = Translate(loaded, UnitId(10), "x") with { Source = "別の" };

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
        var unit = Translate(loaded, UnitId(5), "x");

        Assert.Throws<ArgumentException>(() => Inject(loaded, unit with { Id = "yst99999:000001" }));
        Assert.Throws<ArgumentException>(() => Inject(loaded, unit, unit));
    }

    [Fact]
    public void Inject_DuasUnidadesNoMesmoArgumento_Falha()
    {
        var loaded = Load(FullScript());
        var a = Translate(loaded, UnitId(5), "a");
        var b = a with { Id = UnitId(5, 1) };

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
