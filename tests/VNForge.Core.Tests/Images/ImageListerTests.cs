using System.Security.Cryptography;
using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

public sealed class ImageListerTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "vnforge-images-" + Guid.NewGuid().ToString("N"));

    public ImageListerTests() => Directory.CreateDirectory(_game);

    public void Dispose() => Directory.Delete(_game, recursive: true);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, byte[]>> Archives(params (string File, (string Path, byte[] Data)[] Entries)[] files) =>
        files.ToDictionary(f => f.File, f => (IReadOnlyDictionary<string, byte[]>)f.Entries.ToDictionary(e => e.Path, e => e.Data));

    private ImageListing List(FakeArchiveFormat format, ImageScanOptions? options = null) =>
        ImageLister.List(_game, [format], options, TestContext.Current.CancellationToken);

    [Fact]
    public void Lista_as_imagens_que_o_modelo_le_com_pacote_caminho_tamanho_e_hash()
    {
        var png = SyntheticImages.Png(640, 480);
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("menu/titulo.png", png), ("bg/rua.jpg", SyntheticImages.Jpeg(1280, 720))])));

        var listing = List(format);

        Assert.Equal(["bg/rua.jpg", "menu/titulo.png"], listing.Candidates.Select(c => c.Path).Order(StringComparer.Ordinal));
        var title = listing.Candidates.Single(c => c.Path == "menu/titulo.png");
        Assert.Equal("cg.pak", title.Archive);
        Assert.Equal(new ImageInfo(ImageFormat.Png, 640, 480), title.Info);
        Assert.Equal(png.Length, title.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(png)), title.Hash);
        Assert.Equal(1, listing.ArchivesRead);
        Assert.Empty(listing.Skipped);
    }

    [Fact]
    public void Acha_pacotes_nas_subpastas_e_grava_o_caminho_com_barra()
    {
        FakeArchiveFormat.Touch(_game, "pac/sys/ui.pak");
        var format = new FakeArchiveFormat(Archives(("ui.pak", [("botao.png", SyntheticImages.Png(200, 80))])));

        var listing = List(format);

        Assert.Equal("pac/sys/ui.pak", Assert.Single(listing.Candidates).Archive);
    }

    [Fact]
    public void Ignora_o_que_nao_e_imagem()
    {
        FakeArchiveFormat.Touch(_game, "tudo.pak");
        var format = new FakeArchiveFormat(Archives(("tudo.pak", [("se/porta.ogg", [1, 2, 3]), ("ysbin/yst00001.ybn", [4, 5]), ("a.png", SyntheticImages.Png(100, 100))])));

        var listing = List(format);

        Assert.Equal("a.png", Assert.Single(listing.Candidates).Path);
        Assert.Empty(listing.Skipped);
    }

    [Fact]
    public void Ignora_as_minusculas_pelo_numero_de_pixels()
    {
        FakeArchiveFormat.Touch(_game, "ui.pak");
        var format = new FakeArchiveFormat(Archives(("ui.pak", [("icone.png", SyntheticImages.Png(16, 16)), ("faixa.png", SyntheticImages.Png(150, 20)), ("cg.png", SyntheticImages.Png(800, 600))])));

        var listing = List(format, new ImageScanOptions { MinPixels = 2500 });

        Assert.Equal(["cg.png", "faixa.png"], listing.Candidates.Select(c => c.Path).Order(StringComparer.Ordinal));
        var small = Assert.Single(listing.Skipped);
        Assert.Equal(("icone.png", ImageSkipReason.TooSmall), (small.Path, small.Reason));
    }

    [Fact]
    public void Bmp_e_formato_proprietario_ficam_de_fora_com_o_motivo()
    {
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("a.bmp", SyntheticImages.Bmp(640, 480)), ("b.ycg", [1, 2, 3, 4]), ("c.png", SyntheticImages.Png(640, 480))])));

        var listing = List(format);

        Assert.Equal("c.png", Assert.Single(listing.Candidates).Path);
        Assert.All(listing.Skipped, s => Assert.Equal(ImageSkipReason.UnsupportedFormat, s.Reason));
        Assert.Equal(["a.bmp", "b.ycg"], listing.Skipped.Select(s => s.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Imagem_com_extensao_de_imagem_mas_bytes_invalidos_nao_e_decodificavel()
    {
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("quebrada.png", [0x89, 0x50, 0x4E, 0x47]), ("lixo.jpg", new byte[300])])));

        var listing = List(format);

        Assert.Empty(listing.Candidates);
        Assert.All(listing.Skipped, s => Assert.Equal(ImageSkipReason.NotDecodable, s.Reason));
        Assert.Equal(2, listing.Skipped.Count);
    }

    [Fact]
    public void A_extensao_nao_manda_o_que_vale_e_o_conteudo()
    {
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("falso.bmp", SyntheticImages.Png(300, 300))])));

        var listing = List(format);

        Assert.Equal(ImageFormat.Png, Assert.Single(listing.Candidates).Info.Format);
    }

    [Fact]
    public void Imagem_acima_do_limite_de_bytes_ou_de_lado_fica_de_fora_sem_ler()
    {
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("enorme.png", SyntheticImages.Png(9000, 100)), ("normal.png", SyntheticImages.Png(300, 300))])));

        var listing = List(format, new ImageScanOptions { MaxSide = 8000 });

        Assert.Equal("normal.png", Assert.Single(listing.Candidates).Path);
        var big = Assert.Single(listing.Skipped);
        Assert.Equal(("enorme.png", ImageSkipReason.TooLarge), (big.Path, big.Reason));
    }

    [Fact]
    public void Arquivo_acima_do_limite_de_bytes_fica_de_fora()
    {
        var png = SyntheticImages.Png(400, 400);
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("a.png", png)])));

        var listing = List(format, new ImageScanOptions { MaxBytes = png.Length - 1 });

        Assert.Empty(listing.Candidates);
        Assert.Equal(ImageSkipReason.TooLarge, Assert.Single(listing.Skipped).Reason);
    }

    [Fact]
    public void Teto_de_bytes_padrao_deixa_margem_abaixo_dos_10_MB_em_base64()
    {
        var max = new ImageScanOptions().MaxBytes;

        Assert.Equal(7_400_000, max);
        Assert.True(max / 3 * 4 < 10_000_000);
    }

    [Fact]
    public void Pacote_ilegivel_vira_aviso_e_os_outros_seguem()
    {
        FakeArchiveFormat.Touch(_game, "estragado.pak", "bom.pak");
        var format = new FakeArchiveFormat(Archives(("bom.pak", [("a.png", SyntheticImages.Png(100, 100))])));

        var listing = List(format);

        Assert.Single(listing.Candidates);
        Assert.Equal(1, listing.ArchivesRead);
        Assert.Contains("estragado.pak", Assert.Single(listing.Warnings));
    }

    [Fact]
    public void Pasta_do_jogo_inexistente_e_erro_em_portugues()
    {
        var format = new FakeArchiveFormat(Archives());

        var error = Assert.Throws<VNForgeException>(() => ImageLister.List(Path.Combine(_game, "nao-existe"), [format], null, TestContext.Current.CancellationToken));

        Assert.Contains("A pasta do jogo não existe", error.Message);
    }

    [Fact]
    public void Cancelar_interrompe_a_listagem()
    {
        FakeArchiveFormat.Touch(_game, "cg.pak");
        var format = new FakeArchiveFormat(Archives(("cg.pak", [("a.png", SyntheticImages.Png(100, 100))])));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => ImageLister.List(_game, [format], null, cancelled.Token));
    }
}
