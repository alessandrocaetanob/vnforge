using System.Text;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yscfg;

namespace VNForge.Engines.YuRis.Tests;

/// <summary>O título da janela do jogo no YU-RIS: lido do <c>yscfg.ybn</c> do <c>ysbin.ypf</c> e devolvido ao pacote só com o campo do título trocado (#427).</summary>
public sealed class YuRisGameTitleTests : IDisposable
{
    private const string Title = "テストの題名";
    private const string ConfigEntry = "ysbin/yscfg.ybn";
    private const string ScriptEntry = "ybin/yst00001.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-title-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _cache;
    private readonly YuRisEngine _engine = new();
    private readonly byte[] _script = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290).AddWord("おはよう", 1).AddReturnCode(0, 2).Build().Bytes;

    public YuRisGameTitleTests()
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

    private void WriteGame(byte[]? config)
    {
        var builder = new SyntheticYpfBuilder(500).AddFile("ysc.ybn", Yscm.Bytes).AddFile(ScriptEntry, _script);
        if (config is not null)
        {
            builder.AddFile(ConfigEntry, config);
        }

        File.WriteAllBytes(Path.Combine(_game, "ysbin.ypf"), builder.Build().Bytes);
    }

    private GameTitleFile? Unpack()
    {
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);
        return _engine.ReadTitle(_cache);
    }

    private static byte[] ReadEntry(string ypf, string entryPath)
    {
        using var stream = File.OpenRead(ypf);
        using var archive = (YpfArchive)new YpfArchiveFormat().Open(stream);
        using var content = archive.OpenEntry(archive.Entries.Single(e => e.Path == entryPath));
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void ReadTitle_le_o_titulo_do_yscfg_do_pacote_de_scripts()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));

        var file = Unpack();

        Assert.NotNull(file);
        Assert.Equal(Title, file.Title);
        Assert.Equal(SyntheticYscfgBuilder.Build(Title), file.Content);
        Assert.EndsWith("yscfg.ybn", file.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTitle_de_jogo_sem_yscfg_devolve_nulo()
    {
        WriteGame(null);

        Assert.Null(Unpack());
    }

    [Fact]
    public void ReadTitle_de_yscfg_invalido_recusa_citando_o_arquivo()
    {
        WriteGame([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        _engine.Unpack(_game, _cache, TestContext.Current.CancellationToken);

        var ex = Assert.Throws<InvalidFileException>(() => _engine.ReadTitle(_cache));

        Assert.Contains("yscfg.ybn", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTitle_em_pasta_que_nao_existe_devolve_nulo()
    {
        Assert.Null(_engine.ReadTitle(Path.Combine(_root, "nao-existe")));
    }

    [Fact]
    public void WithTitle_troca_so_o_titulo_com_o_codificador_do_projeto()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;

        var content = _engine.WithTitle(file, "Título novo", TextEncoderRegistry.Default.Create("cp1252"));

        var reread = YscfgReader.Read(content, "yscfg.ybn");
        Assert.Equal(file.Content.AsSpan(0, reread.TitleOffset).ToArray(), content.AsSpan(0, reread.TitleOffset).ToArray());
        Assert.Equal(GameEncodings.Windows1252.GetBytes("Título novo"), content[(reread.TitleOffset + 2)..]);
    }

    [Fact]
    public void WithTitle_recusa_titulo_que_o_codificador_nao_grava()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;

        Assert.Throws<EncoderFallbackException>(() => _engine.WithTitle(file, "Título", TextEncoderRegistry.Default.Create("sjis")));
    }

    [Fact]
    public void WithTitle_com_o_titulo_original_devolve_o_arquivo_identico()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;

        Assert.Equal(file.Content, _engine.WithTitle(file, Title, TextEncoderRegistry.Default.Create("sjis")));
    }

    [Fact]
    public void Repack_devolve_o_yscfg_novo_ao_pacote_e_deixa_o_resto_byte_a_byte_igual()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;
        var content = _engine.WithTitle(file, "New title", TextEncoderRegistry.Default.Create("cp1252"));
        var output = Path.Combine(_root, "build");
        Directory.CreateDirectory(output);

        var result = _engine.Repack(_game, _cache, [new RebuiltScript(file.Path, file.Content, content)], output, cancellationToken: TestContext.Current.CancellationToken);

        var built = Path.Combine(output, "ysbin.ypf");
        Assert.Equal(1, Assert.Single(result.Archives).ReplacedFiles);
        Assert.Equal("New title", YscfgReader.Read(ReadEntry(built, ConfigEntry), "yscfg.ybn").Title);
        Assert.Equal(_script, ReadEntry(built, ScriptEntry));
        Assert.Equal(Yscm.Bytes, ReadEntry(built, "ysc.ybn"));
    }

    [Fact]
    public void Repack_de_jogo_ja_injetado_le_o_pacote_original_indicado_e_nao_o_traduzido_da_pasta()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;
        var gamePackage = Path.Combine(_game, "ysbin.ypf");
        var original = Path.Combine(_root, "backup-ysbin.ypf");
        File.Copy(gamePackage, original);
        var cp1252 = TextEncoderRegistry.Default.Create("cp1252");
        var firstOutput = Path.Combine(_root, "build-1");
        Directory.CreateDirectory(firstOutput);
        _engine.Repack(_game, _cache, [new RebuiltScript(file.Path, file.Content, _engine.WithTitle(file, "First", cp1252))], firstOutput, cancellationToken: TestContext.Current.CancellationToken);
        File.Copy(Path.Combine(firstOutput, "ysbin.ypf"), gamePackage, overwrite: true);
        var output = Path.Combine(_root, "build-2");
        Directory.CreateDirectory(output);

        _engine.Repack(_game, _cache, [new RebuiltScript(file.Path, file.Content, _engine.WithTitle(file, "Second", cp1252))], output, relative => relative == "ysbin.ypf" ? original : gamePackage, TestContext.Current.CancellationToken);

        Assert.Equal("Second", YscfgReader.Read(ReadEntry(Path.Combine(output, "ysbin.ypf"), ConfigEntry), "yscfg.ybn").Title);
        Assert.Equal(_script, ReadEntry(Path.Combine(output, "ysbin.ypf"), ScriptEntry));
    }

    [Fact]
    public void Repack_de_pacote_do_jogo_que_nao_e_o_extraido_continua_recusado_pedindo_vnf_extract()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;
        File.WriteAllBytes(Path.Combine(_game, "ysbin.ypf"), new SyntheticYpfBuilder(500).AddFile("ysc.ybn", Yscm.Bytes).AddFile(ScriptEntry, _script).AddFile(ConfigEntry, SyntheticYscfgBuilder.Build("Outro")).Build().Bytes);
        var output = Path.Combine(_root, "build");
        Directory.CreateDirectory(output);

        var ex = Assert.Throws<VNForgeException>(() => _engine.Repack(_game, _cache, [new RebuiltScript(file.Path, file.Content, file.Content.Reverse().ToArray())], output, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Rode vnf extract de novo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Repack_com_o_titulo_original_gera_um_pacote_com_o_mesmo_conteudo_de_cada_entrada()
    {
        WriteGame(SyntheticYscfgBuilder.Build(Title));
        var file = Unpack()!;
        var output = Path.Combine(_root, "build");
        Directory.CreateDirectory(output);

        _engine.Repack(_game, _cache, [new RebuiltScript(file.Path, file.Content, _engine.WithTitle(file, Title, TextEncoderRegistry.Default.Create("sjis")))], output, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(file.Content, ReadEntry(Path.Combine(output, "ysbin.ypf"), ConfigEntry));
    }
}
