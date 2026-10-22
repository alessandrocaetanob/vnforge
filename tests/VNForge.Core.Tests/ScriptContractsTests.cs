using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Core.Tests;

public class ScriptContractsTests
{
    [Theory]
    [InlineData("yst00123.ybn", 42, "yst00123:0042")]
    [InlineData("YST00123.YBN", 0, "yst00123:0000")]
    [InlineData("ybin/yst00123.ybn", 12345, "yst00123:12345")]
    [InlineData("sem_extensao", 7, "sem_extensao:0007")]
    public void UnitId_junta_a_chave_do_script_e_a_ordem_com_quatro_digitos(string nome, int ordem, string esperado)
    {
        var script = new ScriptFile(nome, new byte[] { 1 });

        Assert.Equal(esperado, script.UnitId(ordem));
    }

    [Fact]
    public void UnitId_recusa_ordem_negativa()
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
    public void ScriptExtraction_sem_avisos_tem_lista_vazia()
    {
        var extracao = new ScriptExtraction([]);

        Assert.Empty(extracao.Units);
        Assert.Empty(extracao.Warnings);
    }

    [Fact]
    public void Engine_de_teste_expoe_scripts_e_um_formato_extrai_e_reconstroi()
    {
        IEngine engine = new FakeEngine();
        var formato = Assert.Single(engine.Scripts);
        var arquivo = new ScriptFile("fake001.txt", Encoding.UTF8.GetBytes("um\ndois"));
        var ctx = new FakeContext();

        var extracao = formato.Extract(arquivo, ctx);
        Assert.Equal(["fake001:0000", "fake001:0001"], extracao.Units.Select(u => u.Id));

        var identico = formato.Rebuild(arquivo, extracao.Units, new FakeEncoder(), ctx);
        Assert.Equal(arquivo.Content.ToArray(), identico);

        var trocadas = extracao.Units
            .Select(u => u.Id.EndsWith("0001", StringComparison.Ordinal)
                ? u with { Translation = "DOIS", Status = TranslationStatus.Translated }
                : u)
            .ToList();
        Assert.Equal("um\nDOIS", Encoding.UTF8.GetString(formato.Rebuild(arquivo, trocadas, new FakeEncoder(), ctx)));
    }

    private sealed class FakeContext() : EngineContext("fake");

    private sealed class FakeEncoder : ITextEncoder
    {
        public string Id => "utf8";

        public byte[] Encode(string text) => Encoding.UTF8.GetBytes(text);
    }

    private sealed class FakeEngine : IEngine
    {
        public string Id => "fake";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts { get; } = [new FakeScriptFormat()];

        public IReadOnlyList<IGamePatch> Patches => [];
    }

    private sealed class FakeScriptFormat : IScriptFormat
    {
        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions => [".txt"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx)
        {
            var linhas = Encoding.UTF8.GetString(file.Content.Span).Split('\n');
            var unidades = linhas
                .Select((l, i) => new TextUnit(file.UnitId(i), TextUnitKind.Dialogue, null, l, null, TranslationStatus.Untranslated))
                .ToList();
            return new ScriptExtraction(unidades);
        }

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx)
        {
            var linhas = Encoding.UTF8.GetString(file.Content.Span).Split('\n');
            foreach (var u in units.Where(u => u.NeedsRewrite))
            {
                linhas[int.Parse(u.Id[(u.Id.IndexOf(':') + 1)..])] = Encoding.UTF8.GetString(encoder.Encode(u.Translation!));
            }

            return Encoding.UTF8.GetBytes(string.Join('\n', linhas));
        }
    }
}
