using System.Text;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Core.Tests;

[Trait("Risco", "R3")]
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
    public void ValidateUnits_recusa_prefixo_parecido_sem_dois_pontos()
    {
        var script = new ScriptFile(Fake001Txt, ReadOnlyMemory<byte>.Empty);

        Assert.Throws<ArgumentException>(() => script.ValidateUnits([Unidade("fake0010:000000")]));
    }

    private static TextUnit Unidade(string id) =>
        new(id, TextUnitKind.Dialogue, null, "x", null, TranslationStatus.Untranslated);

    private sealed class FakeContext() : EngineContext("fake");

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
