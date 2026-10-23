using System.Security.Cryptography;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Engines.YuRis.Tests;

public sealed class YuRisUnpackerTests : IDisposable
{
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
            "ysbin.ypf",
            Archive(("ysc.ybn", Yscm.Bytes), ("yst_list.ybn", Ystl()), ("ybin/yst00001.ybn", Script("おはよう")), ("ybin/yst00002.ybn", Script("こんにちは", "さようなら"))));
        Write("bgm.ypf", Archive(("bgm01.ogg", [1, 2, 3])));
        Write("Jogo.exe", [0x4D, 0x5A]);
    }

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void Unpack_descompacta_so_o_pacote_com_scripts_numa_subpasta_com_o_nome_do_ypf()
    {
        WriteStandardGame();

        var result = _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        var archive = Assert.Single(result.Archives);
        Assert.Equal("ysbin.ypf", archive.GamePath);
        Assert.Equal("ysbin.ypf", archive.Directory);
        Assert.Equal(4, archive.FileCount);
        Assert.True(File.Exists(Path.Combine(_cache, "ysbin.ypf", "ysc.ybn")));
        Assert.True(File.Exists(Path.Combine(_cache, "ysbin.ypf", "ybin", "yst00002.ybn")));
        Assert.False(Directory.Exists(Path.Combine(_cache, "bgm.ypf")));
    }

    [Fact]
    public void Unpack_acha_o_pacote_na_subpasta_pac_e_usa_o_caminho_relativo()
    {
        Write("pac/ysbin.ypf", Archive(("ysc.ybn", Yscm.Bytes), ("yst00001.ybn", Script("おはよう"))));

        var result = _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        Assert.Equal("pac/ysbin.ypf", Assert.Single(result.Archives).Directory);
        Assert.True(File.Exists(Path.Combine(_cache, "pac", "ysbin.ypf", "yst00001.ybn")));
    }

    [Fact]
    public void Unpack_nao_altera_a_pasta_do_jogo()
    {
        WriteStandardGame();
        var before = HashTree(_game);

        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);
        _engine.Open(_cache);

        Assert.Equal(before, HashTree(_game));
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

    [Fact]
    public void Unpack_com_cancelamento_nao_descompacta()
    {
        WriteStandardGame();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => _engine.Unpack(_game, _cache, source.Token));
    }

    [Fact]
    public void Open_lista_os_scripts_ordenados_com_o_nome_legivel_do_yst_list()
    {
        WriteStandardGame();
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        var game = _engine.Open(_cache);

        Assert.IsType<YuRisContext>(game.Context);
        Assert.Equal(["yst00001.ybn", "yst00002.ybn"], game.Scripts.Select(s => s.Name));
        Assert.Equal(["01_prologo", null], game.Scripts.Select(s => s.DisplayName));
        Assert.All(game.Scripts, s => Assert.Same(Assert.Single(_engine.Scripts), s.Format));
        Assert.Empty(game.Warnings);
    }

    [Fact]
    public void Open_sem_yst_list_deixa_os_scripts_sem_nome_legivel_e_sem_aviso()
    {
        Write("ysbin.ypf", Archive(("ysc.ybn", Yscm.Bytes), ("yst00001.ybn", Script("おはよう"))));
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        var game = _engine.Open(_cache);

        Assert.Null(Assert.Single(game.Scripts).DisplayName);
        Assert.Empty(game.Warnings);
    }

    [Fact]
    public void Open_com_yst_list_ilegivel_avisa_e_segue()
    {
        Write("ysbin.ypf", Archive(("ysc.ybn", Yscm.Bytes), ("yst_list.ybn", [1, 2, 3, 4]), ("yst00001.ybn", Script("おはよう"))));
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        var game = _engine.Open(_cache);

        Assert.Single(game.Scripts);
        Assert.Contains("yst_list.ybn", Assert.Single(game.Warnings));
    }

    [Fact]
    public void Open_sem_ysc_ybn_e_erro_em_portugues()
    {
        Directory.CreateDirectory(_cache);

        var ex = Assert.Throws<VNForgeException>(() => _engine.Open(_cache));

        Assert.Contains("ysc.ybn não está em", ex.Message);
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
}
