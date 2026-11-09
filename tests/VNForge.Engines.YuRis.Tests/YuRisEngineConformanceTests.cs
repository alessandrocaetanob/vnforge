using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.ConformanceKit;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Tests;

/// <summary>O YU-RIS no kit de conformidade (#541): só entrega os dados sintéticos, os testes vêm de <see cref="EngineConformanceTests"/>.</summary>
public sealed class YuRisEngineConformanceTests : EngineConformanceTests
{
    private const string Title = "テストの題名";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static SyntheticYstbBuilder NewScript() => new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);

    private static byte[] Sjis(string text) => GameEncodings.ShiftJis.GetBytes(text);

    protected override IEngine CreateEngine() => new YuRisEngine();

    protected override ITextEncoder CreateEncoder() => new ShiftJisTextEncoder();

    protected override EngineContext CreateContext() => new YuRisContext(YscmReader.Read(Yscm.Bytes, "ysc.ybn"));

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
                new ExpectedUnit(TextUnitKind.Dialogue, "美咲", "おはよう") { Translation = "Good morning" },
                new ExpectedUnit(TextUnitKind.Narration, null, "風が吹いた。\\p次") { Translation = "The wind blew.\\pNext", Markers = ["\\p"] },
                new ExpectedUnit(TextUnitKind.Dialogue, "美咲", "（疲れたな）") { Translation = "（So tired）" },
                new ExpectedUnit(TextUnitKind.Choice, null, "はい") { Translation = "Yes" },
                new ExpectedUnit(TextUnitKind.Choice, null, "いいえ") { Translation = "No" },
                new ExpectedUnit(TextUnitKind.Name, null, "美咲"),
                // Sem tradução: o YU-RIS só extrai o argumento de WAIT quando ele tem japonês, e o kit relê o que traduziu.
                new ExpectedUnit(TextUnitKind.Other, null, "一行目\\n二行目"),
            ]),
            new ConformanceScript("ybin/yst00002.ybn", second.Build().Bytes,
            [
                new ExpectedUnit(TextUnitKind.Dialogue, null, "「ただいま」") { Translation = "「I'm home」" },
            ]),
        ];
    }

    protected override string WriteSyntheticTitle(string unpackedDirectory)
    {
        var path = Path.Combine(unpackedDirectory, "ysbin", "yscfg.ybn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SyntheticYscfgBuilder.Build(Title));
        return Title;
    }
}
