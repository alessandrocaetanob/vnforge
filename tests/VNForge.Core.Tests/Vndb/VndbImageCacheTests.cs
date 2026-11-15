using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>O cache de imagens do VNDB (capas e retratos): baixa por um downloader falso, guarda numa pasta do usuário e respeita a privacidade da Biblioteca.</summary>
[Trait("Risco", "R9")]
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
}
