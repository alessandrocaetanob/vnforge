using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Engines.YuRis.Tests;

public sealed class YuRisEngineTests : IDisposable
{
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-engine-" + Guid.NewGuid().ToString("N"));
    private readonly YuRisEngine _engine = new();

    public YuRisEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] Script(uint version = 500, uint key = SyntheticYstbBuilder.KeyV290) =>
        new SyntheticYstbBuilder(Yscm).WithVersion(version).WithKey(key).AddWord("こんにちは", 1).AddReturnCode(0, 2).Build().Bytes;

    private static byte[] Archive(uint ypfVersion, params (string Name, byte[] Content)[] files)
    {
        var builder = new SyntheticYpfBuilder(ypfVersion);
        foreach (var (name, content) in files)
        {
            builder.AddFile(name, content);
        }

        return builder.Build().Bytes;
    }

    private static byte[] ScriptArchive(uint ypfVersion = 500, uint ystbVersion = 500, uint key = SyntheticYstbBuilder.KeyV290) =>
        Archive(ypfVersion, ("ysc.ybn", Yscm.Bytes), ("yst00001.ybn", Script(ystbVersion, key)), ("yst00002.ybn", Script(ystbVersion, key)));

    private void Write(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    [Fact]
    public void Identifica_a_engine_e_expoe_o_formato_ypf()
    {
        Assert.Equal("yuris", _engine.Id);
        Assert.Contains(_engine.Archives, a => a.Id == "ypf");
        Assert.IsAssignableFrom<IEngine>(_engine);
    }

    [Fact]
    public void Pasta_sem_yuris_nao_e_reconhecida()
    {
        Write("jogo.exe", [0x4D, 0x5A, 0x00]);
        Write("data/script.rpy", "label start:"u8.ToArray());

        var result = _engine.Detect(_root);

        Assert.False(result.IsMatch);
        Assert.Equal(string.Empty, result.Summary);
        Assert.Empty(result.Details);
    }

    [Fact]
    public void Pasta_vazia_nao_e_reconhecida()
    {
        Assert.False(_engine.Detect(_root).IsMatch);
    }

    [Fact]
    public void Pasta_inexistente_nao_e_reconhecida_e_nao_lanca()
    {
        var result = _engine.Detect(Path.Combine(_root, "nao-existe"));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Arquivo_ypf_sem_assinatura_nao_e_reconhecido()
    {
        Write("ysbin.ypf", [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.False(_engine.Detect(_root).IsMatch);
    }

    [Fact]
    public void Jogo_sintetico_informa_versoes_arquivos_e_chave()
    {
        Write("ysbin.ypf", ScriptArchive());
        Write("bgm.ypf", Archive(500, ("bgm01.ogg", [1, 2, 3])));
        Write("jogo.exe", [0x4D, 0x5A]);

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains("YU-RIS", result.Summary);
        Assert.Contains("ysbin.ypf v500", result.Summary);
        Assert.Contains("0xD36FAC96", result.Summary);
        Assert.Equal("ysbin.ypf", result.Details["ypfScripts"]);
        Assert.Equal("500", result.Details["ypfVersao"]);
        Assert.Equal("500", result.Details["ystbVersao"]);
        Assert.Equal("0xD36FAC96", result.Details["chaveXor"]);
        Assert.Equal("2", result.Details["scripts"]);
        Assert.Equal("v500, 3 arquivos", result.Details["ypf:ysbin.ypf"]);
        Assert.Equal("v500, 1 arquivo", result.Details["ypf:bgm.ypf"]);
        Assert.False(result.Details.ContainsKey("avisos"));
    }

    [Fact]
    public void Chave_propria_do_jogo_e_derivada_do_script()
    {
        Write("ysbin.ypf", ScriptArchive(key: 0x12345678));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("0x12345678", result.Details["chaveXor"]);
    }

    [Fact]
    public void Acha_os_ypf_na_pasta_pac_e_ignora_maiusculas()
    {
        Write("PAC/YSBIN.YPF", ScriptArchive());

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("PAC/YSBIN.YPF", result.Details["ypfScripts"]);
        Assert.Equal("0xD36FAC96", result.Details["chaveXor"]);
    }

    [Fact]
    public void Ypf_sem_scripts_e_reconhecido_sem_chave()
    {
        Write("bgm.ypf", Archive(500, ("bgm01.ogg", [1, 2, 3])));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.False(result.Details.ContainsKey("chaveXor"));
        Assert.False(result.Details.ContainsKey("ypfScripts"));
        Assert.Contains("bgm.ypf", result.Summary);
    }

    [Fact]
    public void Sem_ysc_ybn_informa_a_versao_do_ystb_mas_nao_a_chave()
    {
        Write("ysbin.ypf", Archive(500, ("yst00001.ybn", Script())));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("500", result.Details["ystbVersao"]);
        Assert.False(result.Details.ContainsKey("chaveXor"));
        Assert.Contains("ysc.ybn", result.Details["avisos"]);
    }

    [Fact]
    public void Pacote_so_com_ysc_ybn_nao_esconde_o_pacote_com_scripts()
    {
        Write("a.ypf", Archive(500, ("ysc.ybn", Yscm.Bytes)));
        Write("b.ypf", Archive(500, ("yst00001.ybn", Script())));

        var result = _engine.Detect(_root);

        Assert.Equal("b.ypf", result.Details["ypfScripts"]);
        Assert.Equal("500", result.Details["ystbVersao"]);
    }

    [Fact]
    public void Ystb_abaixo_da_300_informa_a_versao_e_avisa()
    {
        Write("ysbin.ypf", ScriptArchive(ypfVersion: 290, ystbVersion: 290));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("290", result.Details["ystbVersao"]);
        Assert.False(result.Details.ContainsKey("chaveXor"));
        Assert.True(result.Details.ContainsKey("avisos"));
    }

    [Fact]
    public void Ypf_com_assinatura_e_indice_quebrado_e_reconhecido_com_aviso()
    {
        var bytes = ScriptArchive();
        Write("ysbin.ypf", bytes[..40]);

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains("ysbin.ypf", result.Details["avisos"]);
        Assert.False(result.Details.ContainsKey("chaveXor"));
    }

    [Fact]
    public void Detect_nao_grava_nada_na_pasta()
    {
        Write("ysbin.ypf", ScriptArchive());
        Write("bgm.ypf", Archive(500, ("bgm01.ogg", [1, 2, 3])));
        var before = Snapshot();

        _engine.Detect(_root);

        Assert.Equal(before, Snapshot());
    }

    private List<string> Snapshot() => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(p => File.Exists(p)
            ? $"{p}|{File.GetLastWriteTimeUtc(p).Ticks}|{Convert.ToHexString(File.ReadAllBytes(p))}"
            : p)
        .ToList();
}
