using System.Text;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Core.Tests;

public class ScriptContractsTests
{
    private const string Fake001Txt = "fake001.txt";

    [Fact]
    public void UnitId_recusa_posicao_negativa()
    {
        var script = new ScriptFile("a.bin", ReadOnlyMemory<byte>.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(() => script.UnitId(-1));
    }

    [Fact]
    public void ScriptFile_recusa_nome_vazio()
    {
        Assert.Throws<ArgumentException>(() => new ScriptFile(" ", ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ValidateUnits_recusa_id_de_outro_script()
    {
        var script = new ScriptFile(Fake001Txt, ReadOnlyMemory<byte>.Empty);

        var ex = Assert.Throws<ArgumentException>(() => script.ValidateUnits([Unidade("outro:000000")]));

        Assert.Contains("outro:000000", ex.Message);
    }

    [Fact]
    public void ValidateUnits_recusa_prefixo_parecido_sem_dois_pontos()
    {
        var script = new ScriptFile(Fake001Txt, ReadOnlyMemory<byte>.Empty);

        Assert.Throws<ArgumentException>(() => script.ValidateUnits([Unidade("fake0010:000000")]));
    }

    [Fact]
    public void ValidateUnits_recusa_id_duplicado()
    {
        var script = new ScriptFile(Fake001Txt, ReadOnlyMemory<byte>.Empty);

        var ex = Assert.Throws<ArgumentException>(() => script.ValidateUnits([Unidade(script.UnitId(1)), Unidade(script.UnitId(1))]));

        Assert.Contains("fake001:000001", ex.Message);
    }

    [Fact]
    public void Rebuild_com_caractere_nao_representavel_lanca_citando_o_id_sem_resultado_parcial()
    {
        var formato = new FakeScriptFormat();
        var arquivo = new ScriptFile(Fake001Txt, Encoding.UTF8.GetBytes("um\ndois"));
        var ctx = new FakeContext();
        var unidades = formato.Extract(arquivo, ctx).Units
            .Select(u => u.Id.EndsWith("000001", StringComparison.Ordinal)
                ? u with { Translation = "ação", Status = TranslationStatus.Translated }
                : u)
            .ToList();

        var ex = Assert.Throws<EncoderFallbackException>(() => formato.Rebuild(arquivo, unidades, new AsciiEncoder(), ctx));

        Assert.Contains("fake001:000001", ex.Message);
    }

    [Fact]
    public void Rebuild_recusa_unidade_de_outro_script()
    {
        var formato = new FakeScriptFormat();
        var arquivo = new ScriptFile(Fake001Txt, Encoding.UTF8.GetBytes("um"));

        Assert.Throws<ArgumentException>(() =>
            formato.Rebuild(arquivo, [Unidade("outro:000000")], new FakeEncoder(), new FakeContext()));
    }

    [Fact]
    public void Rebuild_recusa_arquivo_desatualizado_quando_o_trecho_nao_bate_com_o_source()
    {
        var formato = new FakeScriptFormat();
        var original = new ScriptFile(Fake001Txt, Encoding.UTF8.GetBytes("um\ndois"));
        var ctx = new FakeContext();
        var unidades = formato.Extract(original, ctx).Units
            .Select(u => u with { Translation = "x", Status = TranslationStatus.Translated })
            .ToList();
        var outraVersao = new ScriptFile(Fake001Txt, Encoding.UTF8.GetBytes("um\ntres"));

        Assert.Throws<InvalidDataException>(() => formato.Rebuild(outraVersao, unidades, new FakeEncoder(), ctx));
    }

    [Fact]
    public void AsciiEncoder_informa_os_indices_nao_representaveis_sem_lancar()
    {
        var encoder = new AsciiEncoder();

        Assert.Equal([1, 3], encoder.FindUnencodable("aãbç"));
        Assert.Empty(encoder.FindUnencodable("abc"));
        Assert.Throws<EncoderFallbackException>(() => encoder.Encode("ação"));
    }

    private static TextUnit Unidade(string id) =>
        new(id, TextUnitKind.Dialogue, null, "x", null, TranslationStatus.Untranslated);

    private sealed class FakeContext() : EngineContext("fake");

    private class FakeEncoder : ITextEncoder
    {
        public string Id => "utf8";

        public virtual byte[] Encode(string text) => Encoding.UTF8.GetBytes(text);

        public virtual IReadOnlyList<int> FindUnencodable(string text) => [];
    }

    private sealed class AsciiEncoder : FakeEncoder
    {
        private static readonly Encoding Strict = Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public override byte[] Encode(string text) => Strict.GetBytes(text);

        public override IReadOnlyList<int> FindUnencodable(string text) =>
            text.Select((c, i) => (c, i)).Where(t => t.c > 127).Select(t => t.i).ToList();
    }

    private sealed class FakeEngine : IEngine
    {
        public string Id => "fake";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts { get; } = [new FakeScriptFormat()];

        public IReadOnlyList<IGamePatch> Patches => [];
    }

    /// <summary>Script de texto: uma unidade por linha, com <c>{ "line": n }</c> no locator.</summary>
    private sealed class FakeScriptFormat : IScriptFormat
    {
        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions => [".txt"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx)
        {
            var linhas = Encoding.UTF8.GetString(file.Content.Span).Split('\n');
            var unidades = linhas
                .Select((l, i) => new TextUnit(file.UnitId(i), TextUnitKind.Dialogue, null, l, null, TranslationStatus.Untranslated)
                {
                    Locator = JsonSerializer.SerializeToElement(new { line = i }),
                })
                .ToList();
            return new ScriptExtraction(unidades);
        }

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx)
        {
            file.ValidateUnits(units);
            var linhas = Encoding.UTF8.GetString(file.Content.Span).Split('\n');
            var novas = new List<(int Linha, string Texto)>();
            foreach (var u in units.Where(u => u.NeedsRewrite))
            {
                var linha = u.Locator!.Value.GetProperty("line").GetInt32();
                if (linha >= linhas.Length || linhas[linha] != u.Source)
                {
                    throw new InvalidDataException($"A unidade '{u.Id}' não corresponde ao script '{file.Name}': arquivo desatualizado.");
                }

                try
                {
                    novas.Add((linha, Encoding.UTF8.GetString(encoder.Encode(u.Translation!))));
                }
                catch (EncoderFallbackException ex)
                {
                    throw new EncoderFallbackException($"A unidade '{u.Id}' tem caractere que o codificador não representa.", ex);
                }
            }

            foreach (var (linha, texto) in novas)
            {
                linhas[linha] = texto;
            }

            return Encoding.UTF8.GetBytes(string.Join('\n', linhas));
        }
    }
}
