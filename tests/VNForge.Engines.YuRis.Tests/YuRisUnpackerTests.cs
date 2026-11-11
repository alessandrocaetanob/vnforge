using System.Security.Cryptography;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Engines.YuRis.Tests;

public sealed class YuRisUnpackerTests : IDisposable
{
    private const string Yst1Ybn = "yst00001.ybn";
    private const string YsbinYpf = "ysbin.ypf";
    private const string YscYbn = "ysc.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-unpack-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _cache;
    private readonly YuRisEngine _engine = new();

    public YuRisUnpackerTests()
    {
        _game = Path.Combine(_root, "jogo");
        _cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] Script(params string[] lines)
    {
        var builder = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        uint line = 1;
        foreach (var text in lines)
        {
            builder.AddWord(text, line++).AddReturnCode(0, line++);
        }

        return builder.Build().Bytes;
    }

    private static byte[] Ystl() =>
        new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\00_global.yst").AddScript(@"userscript\01_prologo.yst").Build().Bytes;

    private static byte[] Archive(params (string Name, byte[] Content)[] files)
    {
        var builder = new SyntheticYpfBuilder(500);
        foreach (var (name, content) in files)
        {
            builder.AddFile(name, content);
        }

        return builder.Build().Bytes;
    }

    private void Write(string relative, byte[] content)
    {
        var path = Path.Combine(_game, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private void WriteStandardGame()
    {
        Write(
            YsbinYpf,
            Archive((YscYbn, Yscm.Bytes), ("yst_list.ybn", Ystl()), ("ybin/yst00001.ybn", Script("おはよう")), ("ybin/yst00002.ybn", Script("こんにちは", "さようなら"))));
        Write("bgm.ypf", Archive(("bgm01.ogg", [1, 2, 3])));
        Write("Jogo.exe", [0x4D, 0x5A]);
    }

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void Unpack_acha_o_pacote_na_subpasta_pac_e_usa_o_caminho_relativo()
    {
        Write("pac/ysbin.ypf", Archive((YscYbn, Yscm.Bytes), (Yst1Ybn, Script("おはよう"))));

        var result = _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        Assert.Equal("pac/ysbin.ypf", Assert.Single(result.Archives).Directory);
        Assert.True(File.Exists(Path.Combine(_cache, "pac", YsbinYpf, Yst1Ybn)));
    }

    [Fact]
    public void Unpack_sem_pacote_de_scripts_e_erro_em_portugues()
    {
        Write("bgm.ypf", Archive(("bgm01.ogg", [1, 2, 3])));

        var ex = Assert.Throws<VNForgeException>(() => _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken));

        Assert.Contains("Nenhum pacote YPF com scripts", ex.Message);
        Assert.False(Directory.Exists(_cache));
    }

    [Fact]
    public void Unpack_de_pasta_inexistente_e_erro_em_portugues()
    {
        var ex = Assert.Throws<VNForgeException>(() => _engine.Unpack(Path.Combine(_root, "nada"), _cache, TestContext.Current.CancellationToken));

        Assert.Contains("A pasta do jogo não existe", ex.Message);
    }

    private ScriptNames NamesOfOpenedGame() =>
        ScriptNames.Empty.WithDetected(_engine.Open(_cache).Scripts.ToDictionary(
            s => Path.GetFileNameWithoutExtension(s.Name),
            s => new DetectedScript(s.DisplayName, s.Folder),
            StringComparer.Ordinal));

    private static IReadOnlyList<FileStatus> OneLineEach(params string[] keys) =>
        [.. keys.Select(k => new FileStatus(k, new StatusCounts(1, 0, 0, 0, 0)))];

    [Fact]
    public void Open_com_yst_list_ilegivel_avisa_e_segue()
    {
        Write(YsbinYpf, Archive((YscYbn, Yscm.Bytes), ("yst_list.ybn", [1, 2, 3, 4]), (Yst1Ybn, Script("おはよう"))));
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        var game = _engine.Open(_cache);

        Assert.Single(game.Scripts);
        Assert.Contains("yst_list.ybn", Assert.Single(game.Warnings));
    }

    [Fact]
    public void Open_de_pasta_inexistente_manda_rodar_o_extract()
    {
        var ex = Assert.Throws<VNForgeException>(() => _engine.Open(Path.Combine(_root, "nada")));

        Assert.Contains("vnf extract", ex.Message);
    }

    [Fact]
    public void Extract_do_formato_le_o_script_descompactado()
    {
        WriteStandardGame();
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);
        var game = _engine.Open(_cache);
        var script = game.Scripts[1];

        var extraction = script.Format.Extract(new ScriptFile(script.Name, File.ReadAllBytes(script.Path)), game.Context);

        Assert.Equal(["yst00002:000000", "yst00002:000512"], extraction.Units.Select(u => u.Id));
        Assert.Equal(["こんにちは", "さようなら"], extraction.Units.Select(u => u.Source));
        Assert.All(extraction.Units, u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
    }

    [Fact]
    public void Open_sem_ysc_ybn_e_erro_em_portugues()
    {
        Directory.CreateDirectory(_cache);

        var ex = Assert.Throws<VNForgeException>(() => _engine.Open(_cache));

        Assert.Contains("ysc.ybn não está em", ex.Message);
    }
}
