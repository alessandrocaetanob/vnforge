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
    [InlineData(nameof(Webp), ".webp")]
    public async Task A_extensao_segue_o_conteudo_e_nao_o_endereco(string which, string extension)
    {
        var bytes = which switch { nameof(Png) => Png, nameof(Webp) => Webp, _ => Jpeg };
        using var cache = Cache(new FakeDownloader(_ => bytes));

        var path = await cache.GetAsync(new VndbImageRef("https://t.vndb.org/ch/12/3412.bin"), Ct);

        Assert.Equal(extension, Path.GetExtension(path));
    }

    [Fact]
    public async Task A_privacidade_vale_na_hora_da_leitura_e_nao_so_na_do_download()
    {
        var privacy = new LibraryPrivacy(Adult: AdultMode.Show);
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader, () => privacy);
        var image = new VndbImageRef(Portrait, 2, 0);

        Assert.NotNull(await cache.GetAsync(image, Ct));
        Assert.NotNull(cache.Find(image));

        privacy = new LibraryPrivacy(Adult: AdultMode.Hide);

        Assert.Null(cache.Find(image));
        Assert.Null(await cache.GetAsync(image, Ct));
        Assert.Equal(1, downloader.Calls);

        privacy = new LibraryPrivacy(Adult: AdultMode.Show);

        Assert.NotNull(cache.Find(image));
    }

    [Theory]
    [InlineData("http://t.vndb.org/ch/12/3412.jpg")]
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
    public async Task Imagem_rejeitada_some_do_disco_e_so_e_baixada_de_novo_depois_da_espera()
    {
        var time = new FakeTimeProvider();
        var downloader = new FakeDownloader(_ => Jpeg);
        using var cache = Cache(downloader, time: time);
        var image = new VndbImageRef(Portrait);
        var path = await cache.GetAsync(image, Ct);
        Assert.NotNull(path);

        cache.Reject(image);

        Assert.False(File.Exists(path));
        Assert.Null(cache.Find(image));
        Assert.Null(await cache.GetAsync(image, Ct));
        Assert.Equal(1, downloader.Calls);

        time.Advance(VndbImageCache.RetryAfter + TimeSpan.FromSeconds(1));

        Assert.NotNull(await cache.GetAsync(image, Ct));
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
    public void A_pasta_padrao_fica_no_perfil_do_usuario_e_nao_no_projeto()
    {
        var path = VndbImageCache.DefaultDirectory();

        Assert.True(Path.IsPathRooted(path));
        Assert.EndsWith(Path.Combine("VNForge", "cache", "vndb-images"), path, StringComparison.Ordinal);
    }
}
