using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>O cache de imagens do VNDB (capas e retratos): baixa por um downloader falso, guarda numa pasta do usuário e respeita a privacidade da Biblioteca.</summary>
public sealed class VndbImageCacheTests : IDisposable
{
    private const string Portrait = "https://t.vndb.org/ch/12/3412.jpg";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 3];
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 4, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 1, 2];

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeDownloader(Func<Uri, byte[]?> respond) : IImageDownloader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public List<Uri> Requested { get; } = [];

        public async Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            lock (Requested)
            {
                Requested.Add(url);
            }

            await Task.Delay(5, cancellationToken);
            return respond(url);
        }
    }

    private string CacheDir => Path.Combine(_dir.Path, "cache");

    private VndbImageCache Cache(FakeDownloader downloader, Func<LibraryPrivacy>? privacy = null, TimeProvider? time = null) =>
        new(CacheDir, downloader, privacy, time);

    [Fact]
    public async Task Baixa_uma_vez_guarda_na_pasta_e_as_chamadas_seguintes_saem_do_disco()
    {
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader);

        var first = await cache.GetAsync(new VndbImageRef(Portrait), Ct);
        var second = await cache.GetAsync(new VndbImageRef(Portrait), Ct);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(1, downloader.Calls);
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(first, Ct));
        Assert.StartsWith(Path.GetFullPath(CacheDir), Path.GetFullPath(first), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".jpg", Path.GetExtension(first));
    }

    [Fact]
    public async Task Um_cache_novo_na_mesma_pasta_reusa_o_arquivo_sem_baixar()
    {
        var first = new FakeDownloader(_ => Jpeg);
        using (var cache = Cache(first))
        {
            Assert.NotNull(await cache.GetAsync(new VndbImageRef(Portrait), Ct));
        }

        var second = new FakeDownloader(_ => throw new InvalidOperationException("não devia baixar"));
        using var again = Cache(second);

        Assert.NotNull(await again.GetAsync(new VndbImageRef(Portrait), Ct));
        Assert.Equal(0, second.Calls);
    }

    [Theory]
    [InlineData(nameof(Png), ".png")]
    [InlineData(nameof(Webp), ".webp")]
    [InlineData(nameof(Jpeg), ".jpg")]
    public async Task A_extensao_segue_o_conteudo_e_nao_o_endereco(string which, string extension)
    {
        var bytes = which switch { nameof(Png) => Png, nameof(Webp) => Webp, _ => Jpeg };
        using var cache = Cache(new FakeDownloader(_ => bytes));

        var path = await cache.GetAsync(new VndbImageRef("https://t.vndb.org/ch/12/3412.bin"), Ct);

        Assert.Equal(extension, Path.GetExtension(path));
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(2.0, 2.0)]
    public async Task Imagem_que_a_privacidade_esconde_nao_e_baixada_nem_devolvida(double sexual, double violence)
    {
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader, () => new LibraryPrivacy());

        var path = await cache.GetAsync(new VndbImageRef(Portrait, sexual, violence), Ct);

        Assert.Null(path);
        Assert.Equal(0, downloader.Calls);
        Assert.False(Directory.Exists(CacheDir) && Directory.EnumerateFiles(CacheDir, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task A_privacidade_vale_na_hora_da_leitura_e_nao_so_na_do_download()
    {
        var privacy = new LibraryPrivacy(ShowAdult: true);
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader, () => privacy);
        var image = new VndbImageRef(Portrait, 2, 0);

        Assert.NotNull(await cache.GetAsync(image, Ct));
        Assert.NotNull(cache.Find(image));

        privacy = new LibraryPrivacy();

        Assert.Null(cache.Find(image));
        Assert.Null(await cache.GetAsync(image, Ct));
        Assert.Equal(1, downloader.Calls);

        privacy = new LibraryPrivacy(ShowAdult: true);

        Assert.NotNull(cache.Find(image));
    }

    [Fact]
    public async Task Sem_privacidade_informada_vale_o_padrao_que_esconde()
    {
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader);

        Assert.Null(await cache.GetAsync(new VndbImageRef(Portrait, 2, 0), Ct));
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public void Find_nao_usa_a_rede_e_devolve_nulo_quando_a_imagem_nao_esta_no_disco()
    {
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader);

        Assert.Null(cache.Find(new VndbImageRef(Portrait)));
        Assert.Equal(0, downloader.Calls);
    }

    [Theory]
    [InlineData("http://t.vndb.org/ch/12/3412.jpg")]
    [InlineData("https://evil.example/ch/12/3412.jpg")]
    [InlineData("file:///C:/Users/segredo.png")]
    [InlineData("")]
    public async Task Endereco_fora_do_vndb_nao_e_buscado(string url)
    {
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader);

        Assert.Null(await cache.GetAsync(new VndbImageRef(url), Ct));
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task Resposta_que_nao_e_imagem_nao_e_guardada()
    {
        using var cache = Cache(new FakeDownloader(_ => "<html>erro</html>"u8.ToArray()));

        Assert.Null(await cache.GetAsync(new VndbImageRef(Portrait), Ct));
        Assert.False(Directory.Exists(CacheDir) && Directory.EnumerateFiles(CacheDir, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Imagem_acima_do_limite_nao_e_guardada()
    {
        var big = new byte[VndbImageCache.MaxBytes + 1];
        Jpeg.CopyTo(big, 0);
        using var cache = Cache(new FakeDownloader(_ => big));

        Assert.Null(await cache.GetAsync(new VndbImageRef(Portrait), Ct));
    }

    [Fact]
    public async Task Falha_no_download_vira_nulo_e_nao_e_tentada_de_novo_logo_em_seguida()
    {
        var time = new FakeTimeProvider();
        var downloader = new FakeDownloader(_ => null);
        using var cache = Cache(downloader, time: time);
        var image = new VndbImageRef(Portrait);

        Assert.Null(await cache.GetAsync(image, Ct));
        Assert.Null(await cache.GetAsync(image, Ct));
        Assert.Equal(1, downloader.Calls);

        time.Advance(VndbImageCache.RetryAfter + TimeSpan.FromSeconds(1));

        Assert.Null(await cache.GetAsync(image, Ct));
        Assert.Equal(2, downloader.Calls);
    }

    [Fact]
    public async Task Pedidos_ao_mesmo_tempo_da_mesma_imagem_viram_um_download()
    {
        var downloader = new FakeDownloader(_ => Png);
        using var cache = Cache(downloader);

        var paths = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(new VndbImageRef(Portrait), Ct)));

        Assert.Equal(1, downloader.Calls);
        Assert.All(paths, p => Assert.Equal(paths[0], p));
        Assert.NotNull(paths[0]);
    }

    [Fact]
    public async Task Imagens_diferentes_ficam_em_arquivos_diferentes()
    {
        using var cache = Cache(new FakeDownloader(_ => Jpeg));

        var a = await cache.GetAsync(new VndbImageRef("https://t.vndb.org/ch/12/1.jpg"), Ct);
        var b = await cache.GetAsync(new VndbImageRef("https://t.vndb.org/ch/12/2.jpg"), Ct);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Cancelar_a_espera_cancela_a_chamada()
    {
        using var cache = Cache(new FakeDownloader(_ => Jpeg));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(new VndbImageRef(Portrait), cts.Token));
    }

    [Fact]
    public async Task O_arquivo_gravado_nao_deixa_temporarios_na_pasta()
    {
        using var cache = Cache(new FakeDownloader(_ => Jpeg));
        await cache.GetAsync(new VndbImageRef(Portrait), Ct);

        Assert.DoesNotContain(Directory.EnumerateFiles(CacheDir, "*", SearchOption.AllDirectories), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pasta_padrao_fica_no_perfil_do_usuario_e_nao_no_projeto()
    {
        var path = VndbImageCache.DefaultDirectory();

        Assert.True(Path.IsPathRooted(path));
        Assert.EndsWith(Path.Combine("VNForge", "cache", "vndb-images"), path, StringComparison.Ordinal);
    }
}
