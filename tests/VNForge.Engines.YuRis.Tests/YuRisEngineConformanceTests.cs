using System.Buffers.Binary;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.ConformanceKit;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Ystb;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Tests;

/// <summary>O YU-RIS no kit de conformidade (#541): só entrega os dados sintéticos, os testes vêm de <see cref="EngineConformanceTests"/>.</summary>
public sealed class YuRisEngineConformanceTests : EngineConformanceTests
{
    private const string Title = "テストの題名";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YscmFile Commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");

    private static SyntheticYstbBuilder NewScript() => new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);

    private static byte[] Sjis(string text) => GameEncodings.ShiftJis.GetBytes(text);

    protected override IEngine CreateEngine() => new YuRisEngine();

    protected override ITextEncoder CreateEncoder() => new ShiftJisTextEncoder();

    protected override EngineContext CreateContext() => new YuRisContext(Commands);

    protected override void WriteSyntheticGame(string directory)
    {
        var scripts = CreateScripts();
        var archive = new SyntheticYpfBuilder(500).AddFile("ysc.ybn", Yscm.Bytes);
        foreach (var script in scripts)
        {
            archive.AddFile(script.Name, script.Content);
        }

        File.WriteAllBytes(Path.Combine(directory, "ysbin.ypf"), archive.Build().Bytes);
        File.WriteAllBytes(Path.Combine(directory, "jogo.exe"), [0x4D, 0x5A]);
    }

    protected override IReadOnlyList<ConformanceScript> CreateScripts()
    {
        var first = NewScript();
        first.AddWord("美咲「おはよう」", 1);
        first.AddReturnCode(0, 2);
        first.AddWordBytes([.. Sjis("風が吹いた。"), 0xEF, 0xF2, .. Sjis("次")], 3);
        first.AddReturnCode(0, 4);
        first.AddWord("美咲（疲れたな）", 5);
        first.AddCommand("GOSUB", 6, first.StringExpression("ES.SEL.SET"), first.StringExpression("はい"), first.StringExpression("いいえ"));
        first.AddCommand("GOSUB", 7, first.StringExpression("es.char.name"), first.StringExpression("美咲"));
        first.AddCommand("WAIT", 8, first.StringExpression("一行目\\n二行目"));

        var second = NewScript();
        second.AddWord("「ただいま」", 1);
        second.AddReturnCode(0, 2);

        return
        [
            new ConformanceScript("ybin/yst00001.ybn", first.Build().Bytes,
            [
                new ExpectedUnit(TextUnitKind.Dialogue, "美咲", "おはよう") { Translation = "Good morning", SpeakerTranslation = "Misaki" },
                new ExpectedUnit(TextUnitKind.Narration, null, "風が吹いた。\\p次") { Translation = "The wind blew.\\pNext", Markers = ["\\p"] },
                new ExpectedUnit(TextUnitKind.Dialogue, "美咲", "（疲れたな）") { Translation = "（So tired）", SpeakerTranslation = "Misaki" },
                new ExpectedUnit(TextUnitKind.Choice, null, "はい") { Translation = "Yes" },
                new ExpectedUnit(TextUnitKind.Choice, null, "いいえ") { Translation = "No" },
                // O YU-RIS só extrai o argumento de um comando quando ele tem japonês, e o kit relê o que traduziu: as duas traduções mantêm japonês.
                new ExpectedUnit(TextUnitKind.Name, null, "美咲") { Translation = "美咲さん" },
                new ExpectedUnit(TextUnitKind.Other, null, "一行目\\n二行目") { Translation = "一行目\\n三行目", Markers = ["\\n"] },
            ]),
            new ConformanceScript("ybin/yst00002.ybn", second.Build().Bytes,
            [
                new ExpectedUnit(TextUnitKind.Dialogue, null, "「ただいま」") { Translation = "「I'm home」" },
            ]),
        ];
    }

    private const int HeaderBytes = 32;
    private const int ArgumentBytes = 12;
    private const int ArgumentFixedBytes = 4;

    /// <summary>
    /// As seções do YSTB (docs/06): cabeçalho de 32 bytes, instruções, argumentos de 12 bytes, recursos e números de linha. Só mudam o tamanho dos
    /// recursos (bytes 20 a 23 do cabeçalho), o <c>size</c> e o <c>offset</c> dos argumentos que a tradução reescreve e o fim dos recursos, onde o
    /// escritor acrescenta o texto novo depois do antigo. As seções são cifradas por XOR seção a seção, então comparar os bytes cifrados equivale a
    /// comparar os decifrados.
    /// </summary>
    protected override IReadOnlyList<ScriptRegion> DescribeScript(ScriptFile script, IReadOnlyList<TextUnit> units)
    {
        var content = script.Content;
        var file = YstbReader.Read(content, script.Name, Commands);
        var owners = ArgumentOwners(file, units);
        var instructionsLength = ReadLength(content, 12);
        var argumentsLength = ReadLength(content, 16);
        var resourcesLength = ReadLength(content, 20);
        var lineNumbersLength = ReadLength(content, 24);
        Assert.Equal(HeaderBytes + instructionsLength + argumentsLength + resourcesLength + lineNumbersLength, content.Length);

        var regions = new List<ScriptRegion>
        {
            new("cabeçalho", content[..20]),
            new("tamanho dos recursos", content.Slice(20, 4)) { Shared = true },
            new("cabeçalho (fim)", content.Slice(24, HeaderBytes - 24)),
            new("instruções", content.Slice(HeaderBytes, instructionsLength)),
        };

        var argumentsStart = HeaderBytes + instructionsLength;
        for (var i = 0; i < argumentsLength / ArgumentBytes; i++)
        {
            var cell = content.Slice(argumentsStart + (i * ArgumentBytes), ArgumentBytes);
            regions.Add(new($"argumento {i} (id, tipo e operador)", cell[..ArgumentFixedBytes]));
            regions.Add(new($"argumento {i} (size e offset)", cell[ArgumentFixedBytes..]) { Unit = owners.TryGetValue(i, out var owner) ? owner : null });
        }

        var resourcesStart = argumentsStart + argumentsLength;
        regions.Add(new("recursos", content.Slice(resourcesStart, resourcesLength)) { Appends = true });
        regions.Add(new("números de linha", content[(resourcesStart + resourcesLength)..]));
        return regions;
    }

    private static int ReadLength(ReadOnlyMemory<byte> content, int offset) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(content.Span[offset..]));

    private static Dictionary<int, int> ArgumentOwners(YstbFile file, IReadOnlyList<TextUnit> units)
    {
        var owners = new Dictionary<int, int>();
        for (var unit = 0; unit < units.Count; unit++)
        {
            foreach (var part in units[unit].Locator!.Value.GetProperty("parts").EnumerateArray())
            {
                var instruction = file.Instructions[part.GetProperty("instruction").GetInt32()];
                owners.Add(instruction.FirstArgumentIndex + part.GetProperty("argument").GetInt32(), unit);
            }
        }

        return owners;
    }

    protected override string WriteSyntheticTitle(string unpackedDirectory)
    {
        var path = Path.Combine(unpackedDirectory, "ysbin", "yscfg.ybn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SyntheticYscfgBuilder.Build(Title));
        return Title;
    }
}
