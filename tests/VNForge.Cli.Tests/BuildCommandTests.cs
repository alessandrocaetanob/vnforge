using System.Security.Cryptography;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R3")]
[Trait("Risco", "R4")]
public sealed class BuildCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string Yst00001Ybn = "yst00001.ybn";
    private const string ProjectOption = "--project";
    private const string BuildVerb = "build";
    private const string Yst00001000000 = "yst00001:000000";

    private const string GoodMorning = "Good morning";

    private const string Hello = "Hello";

    private const string ForbiddenWord = "palavra-proibida";

    private const string AllowWarningsFlag = "--allow-warnings";

    private const string EncodingFlag = "--encoding";

    private const string JsonFlag = "--json";

    private const string Yst2 = "yst00002";

    private const string Yst1 = "yst00001";

    private const string ReportFlag = "--report";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly byte[] _scriptsYpf;
    private readonly byte[] _extraYpf;

    public BuildCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        _scriptsYpf = ScriptArchive();
        _extraYpf = ExtraArchive();
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), _scriptsYpf);
        File.WriteAllBytes(Path.Combine(_game, "bgm.ypf"), new SyntheticYpfBuilder(500).AddFile("bgm01.ogg", [1, 2, 3]).Build().Bytes);
    }

    public void Dispose() => _cli.Dispose();

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private static SyntheticYstbBuilder NewScript() => new(Yscm);

    private static byte[] ScriptArchive()
    {
        var first = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));

        var second = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        second.AddWord("雨が降っていた。", 1).AddReturnCode(0, 2);
        second.AddCommand("GOSUB", 3, second.StringExpression("ES.SEL.SET"), second.StringExpression("はい"), second.StringExpression("いいえ"));

        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").AddScript(@"userscript\01_prologo.yst").Build();
        return new SyntheticYpfBuilder(500)
            .AddFile("ysc.ybn", Yscm.Bytes)
            .AddFile("yst_list.ybn", list.Bytes)
            .AddFile(Yst00001Ybn, first.Build().Bytes)
            .AddFile("yst00002.ybn", second.Build().Bytes)
            .Build().Bytes;
    }

    private static byte[] ExtraArchive()
    {
        var third = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        third.AddWord("夜になった。", 1).AddReturnCode(0, 2);
        return new SyntheticYpfBuilder(500).AddFile("yst00003.ybn", third.Build().Bytes).Build().Bytes;
    }

    private async Task InitAndExtract(bool withExtraArchive = false)
    {
        if (withExtraArchive)
        {
            Directory.CreateDirectory(Path.Combine(_game, "pac"));
            await File.WriteAllBytesAsync(Path.Combine(_game, "pac", "extra.ypf"), _extraYpf, TestContext.Current.CancellationToken);
        }

        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        UseStrategy("sjis");
        var extract = await _cli.RunAsync(["extract", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        Assert.Equal(0, extract.ExitCode);
    }

    private void UseStrategy(string strategy)
    {
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveConfig(project.LoadConfig() with { Encoding = new EncodingConfig(strategy) });
    }

    private Task<RunResult> Build(params string[] args) =>
        _cli.RunAsync([BuildVerb, .. args, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

    private void Translate(string key, Func<int, string> translation)
    {
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText(key).Select((u, i) => u with { Translation = translation(i), Status = TranslationStatus.Translated }).ToList();
        project.SaveText(key, units);
    }

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    private static Dictionary<string, byte[]> ReadEntries(string ypfPath)
    {
        using var stream = File.OpenRead(ypfPath);
        using var archive = (YpfArchive)new YpfArchiveFormat().Open(stream);
        return archive.Entries.ToDictionary(e => e.Path, e =>
        {
            using var content = archive.OpenEntry(e);
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return buffer.ToArray();
        });
    }

    private static IReadOnlyList<TextUnit> Reextract(string name, byte[] bytes)
    {
        var commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");
        var file = new ScriptFile(name, bytes);
        var script = YstbReader.Read(bytes, name, commands);
        return YstbTextExtractor.Extract(file, script, commands).Units;
    }

    [Fact]
    public async Task Sem_traducao_o_ysbin_ypf_do_build_e_identico_ao_original()
    {
        await InitAndExtract();

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var built = Path.Combine(ProjectPath, BuildVerb, YsbinYpf);
        Assert.Equal(_scriptsYpf, await File.ReadAllBytesAsync(built, TestContext.Current.CancellationToken));
        Assert.Equal([YsbinYpf], Directory.EnumerateFiles(Path.Combine(ProjectPath, BuildVerb), "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Path.Combine(ProjectPath, BuildVerb), f)));
    }

    private sealed class TranslationIs(string id, QaSeverity severity, string text) : UnitQaRule
    {
        public override string Id => id;

        public override string Description => "Teste.";

        public override QaSeverity Severity => severity;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == text)
            {
                yield return $"tradução '{text}' recusada";
            }
        }
    }

    private async Task<RunResult> BuildWithRule(QaSeverity severity, params string[] args)
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? Hello : "Mika");
        _cli.Cli.Qa = new QaEngine([new TranslationIs(ForbiddenWord, severity, Hello)]);
        return await Build(args);
    }

    [Fact]
    public async Task Caractere_fora_do_shift_jis_aborta_citando_o_id_e_nao_toca_no_build_anterior()
    {
        await InitAndExtract();
        var first = await Build();
        Assert.Equal(0, first.ExitCode);
        var build = Path.Combine(ProjectPath, BuildVerb);
        var before = HashTree(build);
        Translate(Yst1, i => i == 0 ? "Bom dia 😀" : "Mika");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Yst00001000000, result.Error);
        Assert.Equal(before, HashTree(build));
        Assert.False(Directory.Exists(build + ".tmp"));
    }
}
