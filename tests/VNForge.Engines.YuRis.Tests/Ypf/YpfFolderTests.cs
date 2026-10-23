using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public sealed class YpfFolderTests : IDisposable
{
    private static readonly YpfArchiveFormat Format = new();
    private static readonly byte[] Repetitive = Enumerable.Repeat((byte)'A', 4000).ToArray();
    private static readonly byte[] Small = [0x10, 0x20, 0x30, 0x40, 0x50];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ypffolder-" + Guid.NewGuid().ToString("N"));

    public YpfFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    public static TheoryData<uint> Versions => new() { 200u, 300u, 500u };

    private static SyntheticYpf Sample(uint version) => new SyntheticYpfBuilder(version)
        .AddFile(@"ysbin\yst00001.ybn", Small)
        .AddFile(@"bg\fundo.png", Repetitive, SyntheticYpfFileType.Png, compress: true)
        .AddFile(@"se\porta.ogg", [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
        .AddFile(@"ysbin\vazio.ybn", [])
        .Build();

    private static YpfArchive Open(byte[] bytes) => (YpfArchive)Format.Open(new MemoryStream(bytes));

    private string Dir(string name) => Path.Combine(_root, name);

    private static byte[] PackToBytes(YpfArchive archive, string folder, out YpfPackResult result)
    {
        using var output = new MemoryStream();
        result = YpfFolder.Pack(archive, folder, output, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    [Fact]
    public void Unpack_GravaCadaEntradaDescomprimidaNaPasta()
    {
        var ypf = Sample(500);
        using var archive = Open(ypf.Bytes);

        var result = YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken);

        Assert.Equal(4, result.FileCount);
        Assert.Equal(Small.Length + Repetitive.Length + 9, result.ByteCount);
        Assert.Equal(Small, File.ReadAllBytes(Path.Combine(Dir("out"), "ysbin", "yst00001.ybn")));
        Assert.Equal(Repetitive, File.ReadAllBytes(Path.Combine(Dir("out"), "bg", "fundo.png")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(Dir("out"), "ysbin", "vazio.ybn")));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void UnpackEPackSemMudancasGeramArquivoIdentico(uint version)
    {
        var ypf = Sample(version);
        using var archive = Open(ypf.Bytes);
        YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken);

        var packed = PackToBytes(archive, Dir("out"), out var result);

        Assert.Equal(ypf.Bytes, packed);
        Assert.Empty(result.Replaced);
        Assert.Empty(result.Missing);
        Assert.Equal(4, result.EntryCount);
    }

    [Fact]
    public void Pack_ArquivoAlteradoSaiNoNovoYpfEOsOutrosFicamIguais()
    {
        var ypf = Sample(500);
        using var archive = Open(ypf.Bytes);
        YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken);
        var changed = Enumerable.Repeat((byte)'Z', 6000).ToArray();
        File.WriteAllBytes(Path.Combine(Dir("out"), "bg", "fundo.png"), changed);

        var packed = PackToBytes(archive, Dir("out"), out var result);

        Assert.Equal(["bg/fundo.png"], result.Replaced);
        using var reopened = Open(packed);
        var image = Assert.Single(reopened.Entries, e => e.Path == "bg/fundo.png");
        using (var content = reopened.OpenEntry(image))
        using (var copy = new MemoryStream())
        {
            content.CopyTo(copy);
            Assert.Equal(changed, copy.ToArray());
        }

        var script = Assert.Single(archive.Entries, e => e.Path == "ysbin/yst00001.ybn");
        var scriptAfter = Assert.Single(reopened.Entries, e => e.Path == "ysbin/yst00001.ybn");
        Assert.Equal(
            ypf.Bytes[(int)script.Offset..(int)(script.Offset + script.SizeStored)],
            packed[(int)scriptAfter.Offset..(int)(scriptAfter.Offset + scriptAfter.SizeStored)]);
    }

    [Fact]
    public void Pack_ArquivoQueOYpfNaoTemFalhaSemGravarNada()
    {
        var ypf = Sample(500);
        using var archive = Open(ypf.Bytes);
        YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken);
        File.WriteAllBytes(Path.Combine(Dir("out"), "novo.txt"), [1]);
        using var output = new MemoryStream();

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Pack(archive, Dir("out"), output, TestContext.Current.CancellationToken));

        Assert.Contains("novo.txt", ex.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Pack_ArquivoAusenteNaPastaMantemAEntradaOriginal()
    {
        var ypf = Sample(500);
        using var archive = Open(ypf.Bytes);
        YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(Dir("out"), "se", "porta.ogg"));

        var packed = PackToBytes(archive, Dir("out"), out var result);

        Assert.Equal(ypf.Bytes, packed);
        Assert.Equal(["se/porta.ogg"], result.Missing);
    }

    [Fact]
    public void Pack_PastaInexistenteFalha()
    {
        using var archive = Open(Sample(500).Bytes);
        using var output = new MemoryStream();

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Pack(archive, Dir("nao-existe"), output, TestContext.Current.CancellationToken));

        Assert.Contains("nao-existe", ex.Message);
    }

    [Theory]
    [InlineData(@"..\fora.txt")]
    [InlineData(@"a\..\..\fora.txt")]
    [InlineData(@"C:\fora.txt")]
    [InlineData(@"\fora.txt")]
    [InlineData(@"a\\b.txt")]
    public void Unpack_RecusaCaminhoQueSaiDaPastaSemGravarNada(string name)
    {
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(@"ok\a.txt", Small)
            .AddFile(name, Small)
            .Build();
        using var archive = Open(ypf.Bytes);

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken));

        Assert.Contains("caminho", ex.Message);
        Assert.False(Directory.Exists(Dir("out")));
        Assert.False(File.Exists(Path.Combine(_root, "fora.txt")));
    }

    [Fact]
    public void Unpack_RecusaEntradaQueEArquivoEPastaDeOutraSemGravarNada()
    {
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile("a", Small)
            .AddFile(@"a", Small)
            .Build();
        using var archive = Open(ypf.Bytes);

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken));

        Assert.Contains("pasta", ex.Message);
        Assert.False(Directory.Exists(Dir("out")));
    }

    [Fact]
    public void Unpack_HashAdulteradoViraErroEmPortuguesComOCaminhoDaEntrada()
    {
        using var archive = Open(Sample(500).WithWrongDataHash(@"se\porta.ogg"));

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken));

        Assert.Contains("se/porta.ogg", ex.Message);
    }

    [Fact]
    public void Pack_HashAdulteradoViraErroEmPortuguesComOCaminhoDaEntrada()
    {
        using (var good = Open(Sample(500).Bytes))
        {
            YpfFolder.Unpack(good, Dir("out"), TestContext.Current.CancellationToken);
        }

        using var archive = Open(Sample(500).WithWrongDataHash(@"se\porta.ogg"));
        using var output = new MemoryStream();

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Pack(archive, Dir("out"), output, TestContext.Current.CancellationToken));

        Assert.Contains("se/porta.ogg", ex.Message);
    }

    [Fact]
    public void Unpack_DestinoQueEUmArquivoViraErroEmPortugues()
    {
        File.WriteAllText(Dir("destino"), "x");
        using var archive = Open(Sample(500).Bytes);

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("destino"), TestContext.Current.CancellationToken));

        Assert.Contains("Não foi possível gravar", ex.Message);
    }

    [Fact]
    public void Unpack_PastaNoLugarDeUmArquivoViraErroEmPortugues()
    {
        Directory.CreateDirectory(Path.Combine(Dir("out"), "bg", "fundo.png"));
        using var archive = Open(Sample(500).Bytes);

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken));

        Assert.Contains("fundo.png", ex.Message);
    }

    [Fact]
    public void Unpack_RecusaLinkSimbolicoNoDestinoSemGravarNada()
    {
        var elsewhere = Dir("fora");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(Dir("out"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Dir("out"), "bg"), elsewhere);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Este sistema não permite criar link simbólico.");
        }

        using var archive = Open(Sample(500).Bytes);

        var error = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken));

        Assert.Contains("link simbólico", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
        Assert.False(File.Exists(Path.Combine(Dir("out"), "ysbin", "yst00001.ybn")));
    }

    [Fact]
    public void Unpack_AceitaRaizQueELinkSimbolico()
    {
        Directory.CreateDirectory(Dir("real"));
        try
        {
            Directory.CreateSymbolicLink(Dir("link"), Dir("real"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Este sistema não permite criar link simbólico.");
        }

        using var archive = Open(Sample(500).Bytes);

        YpfFolder.Unpack(archive, Dir("link"), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(Dir("real"), "se", "porta.ogg")));
    }

    [Fact]
    public void Unpack_RecusaEntradaGrandeDemaisPeloIndiceSemGravarNada()
    {
        using var archive = Open(Sample(500).WithUncompressedSize(@"bg\fundo.png", uint.MaxValue));

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken));

        Assert.Contains("grande demais", ex.Message);
        Assert.False(Directory.Exists(Dir("out")));
    }

    [Fact]
    public void Pack_ArquivoDesconhecidoCitaONomeEComoResolver()
    {
        using var archive = Open(Sample(500).Bytes);
        Directory.CreateDirectory(Dir("out"));
        File.WriteAllBytes(Path.Combine(Dir("out"), "Thumbs.db"), [1]);
        using var output = new MemoryStream();

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Pack(archive, Dir("out"), output, TestContext.Current.CancellationToken));

        Assert.Contains("\"Thumbs.db\"", ex.Message);
        Assert.Contains("remova", ex.Message);
    }

    [Fact]
    public void Pack_NoWindowsComparaCaminhosSemDiferenciarCaixa()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Só o Windows ignora a caixa dos nomes.");
        using var archive = Open(Sample(500).Bytes);
        var folder = Path.Combine(Dir("out"), "BG");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "FUNDO.PNG"), Repetitive);
        using var output = new MemoryStream();

        var result = YpfFolder.Pack(archive, Dir("out"), output, TestContext.Current.CancellationToken);

        Assert.Empty(result.Replaced);
        Assert.Equal(3, result.Missing.Count);
    }

    [Fact]
    public void Pack_NoWindowsRecusaOriginalComEntradasQueSoDiferemNaCaixa()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Só o Windows ignora a caixa dos nomes.");
        var ypf = new SyntheticYpfBuilder(500).AddFile("a.txt", Small).AddFile("A.TXT", Small).Build();
        using var archive = Open(ypf.Bytes);
        Directory.CreateDirectory(Dir("out"));
        using var output = new MemoryStream();

        var ex = Assert.Throws<VNForgeException>(() => YpfFolder.Pack(archive, Dir("out"), output, TestContext.Current.CancellationToken));

        Assert.Contains("só diferem na caixa", ex.Message);
    }

    [Fact]
    public void Unpack_PreservaNomesEmJapones()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile("シナリオ\\第一章.ybn", Small).Build();
        using var archive = Open(ypf.Bytes);

        YpfFolder.Unpack(archive, Dir("out"), TestContext.Current.CancellationToken);

        Assert.Equal(Small, File.ReadAllBytes(Path.Combine(Dir("out"), "シナリオ", "第一章.ybn")));
    }
}
