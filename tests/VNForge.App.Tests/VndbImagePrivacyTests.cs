using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Screens;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests;

/// <summary>
/// A privacidade da Biblioteca nas imagens do VNDB (#580): o leitor do <c>library.json</c>, a fonte de imagens que o consulta antes de decidir e os controles na tela, que
/// escondem a imagem quando o ajuste muda por fora (a CLI). Também a posse dos bitmaps e o controle reciclado com uma carga em andamento. Nenhum teste abre rede.
/// </summary>
public sealed class VndbImagePrivacyTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly VndbImageRef AdultFace = new("https://t.vndb.org/ch/12/9999.jpg", Sexual: 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static long writes;

    /// <summary>Mostrar (verdadeiro) ou Esconder (falso): o Desfocar baixa a imagem e a tela a borra, então só o Esconder a tira da tela.</summary>
    private static void SetAdult(string directory, bool showAdult) => SetMode(directory, showAdult ? AdultMode.Show : AdultMode.Hide);

    private static void SetMode(string directory, AdultMode mode)
    {
        new LibraryFile(directory).Update(library => library.Privacy = new LibraryPrivacy(Adult: mode));

        // "show", "blur" e "hide" têm o mesmo tamanho: o leitor só nota a troca pela data do arquivo, que duas gravações seguidas podem dividir.
        File.SetLastWriteTimeUtc(Path.Combine(directory, LibraryFile.FileName), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Interlocked.Increment(ref writes)));
    }

    private static byte[] Png(int width = 16, int height = 16)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            var pixels = new byte[frame.RowBytes * height];
            Array.Fill(pixels, (byte)200);
            Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private sealed class FakeDownloader(Func<Uri, byte[]?> respond) : IImageDownloader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(respond(url));
        }
    }

    private static async Task WaitUntil(Func<bool> condition, Window? window = null)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            if (window is not null)
            {
                ScreenHost.Settle(window);
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "a condição não chegou a valer");
    }

    // ----- LibraryPrivacyReader -----

    [Fact]
    public void Arquivo_ausente_ou_ilegivel_vale_o_padrao_e_a_proxima_leitura_tenta_de_novo()
    {
        using var temp = new TempFolder();
        using var reader = new LibraryPrivacyReader(temp.Path, watch: false);

        Assert.False(reader.Refresh().ShowAdult);

        File.WriteAllText(Path.Combine(temp.Path, LibraryFile.FileName), "isto não é json");
        Assert.False(reader.Refresh().ShowAdult);

        File.Delete(Path.Combine(temp.Path, LibraryFile.FileName));
        SetAdult(temp.Path, showAdult: true);
        Assert.True(reader.Refresh().ShowAdult);
    }

    [Fact]
    public async Task O_observador_atualiza_e_avisa_quando_outro_processo_grava_o_library_json()
    {
        using var temp = new TempFolder();
        SetAdult(temp.Path, showAdult: true);
        using var reader = new LibraryPrivacyReader(temp.Path);
        reader.Refresh();
        var changed = new TaskCompletionSource();
        reader.Changed += (_, _) => changed.TrySetResult();

        SetAdult(temp.Path, showAdult: false);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.False(reader.Current().ShowAdult);
    }

    // ----- VndbImageSource -----

    private static (VndbImageSource Source, LibraryPrivacyReader Reader, FakeDownloader Downloader, FakeTimeProvider Time) NewSource(string config, string cache, Func<Uri, byte[]?>? respond = null, int capacity = 160, bool watch = false)
    {
        var reader = new LibraryPrivacyReader(config, watch);
        var downloader = new FakeDownloader(respond ?? (_ => Png()));
        var time = new FakeTimeProvider();
        return (new VndbImageSource(new VndbImageCache(cache, downloader, reader.Current, time), reader, capacity), reader, downloader, time);
    }

    [Fact]
    public async Task Imagem_adulta_na_tela_volta_ao_marcador_quando_o_ajuste_e_desligado()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                SetAdult(temp.Path, showAdult: true);
                var (source, reader, _, _) = NewSource(temp.Path, Path.Combine(temp.Path, "imagens"));
                VndbImages.Source = source;
                var portrait = new Portrait { Size = PortraitSize.Md, Image = AdultFace, Seed = "Proibida" };
                var window = new Window { Width = 200, Height = 200, Content = portrait };
                try
                {
                    reader.Refresh();
                    window.Show();
                    await WaitUntil(() => portrait.HasImage, window);

                    SetAdult(temp.Path, showAdult: false);
                    reader.Refresh();
                    await WaitUntil(() => !portrait.HasImage, window);

                    SetAdult(temp.Path, showAdult: true);
                    reader.Refresh();
                    await WaitUntil(() => portrait.HasImage, window);
                }
                finally
                {
                    window.Close();
                    VndbImages.Source = null;
                    source.Dispose();
                }

                return 0;
            },
            Ct);
    }

    [Fact]
    public async Task Sem_a_pasta_de_configuracao_na_partida_ligar_e_desligar_o_adulto_esconde_a_imagem_sem_ninguem_chamar_Refresh()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var config = Path.Combine(temp.Path, "cfg");
                var (source, _, _, _) = NewSource(config, Path.Combine(temp.Path, "imagens"), watch: true);
                VndbImages.Source = source;
                var portrait = new Portrait { Size = PortraitSize.Md, Image = AdultFace, Seed = "Proibida" };
                var window = new Window { Width = 200, Height = 200, Content = portrait };
                try
                {
                    Assert.True(Directory.Exists(config), "o leitor cria a pasta antes de observar");
                    window.Show();
                    ScreenHost.Settle(window);
                    // Sem arquivo vale o padrão, Blur: a imagem aparece, desfocada. O download e a decodificação rodam fora da UI, então a imagem chega depois do Show.
                    await WaitUntil(() => portrait.HasImage && portrait.IsBlurred, window);

                    SetAdult(config, showAdult: true);
                    await WaitUntil(() => portrait.HasImage && !portrait.IsBlurred, window);

                    SetAdult(config, showAdult: false);
                    await WaitUntil(() => !portrait.HasImage, window);
                }
                finally
                {
                    window.Close();
                    VndbImages.Source = null;
                    source.Dispose();
                }

                return 0;
            },
            Ct);
    }

    [Fact]
    public async Task O_ajuste_desligado_durante_o_download_nao_deixa_a_posse_sair()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                SetAdult(temp.Path, showAdult: true);
                var (source, _, downloader, _) = NewSource(temp.Path, Path.Combine(temp.Path, "imagens"), _ =>
                {
                    SetAdult(temp.Path, showAdult: false);
                    return Png();
                });
                using (source)
                {
                    Assert.Null(await source.LoadAsync(AdultFace, Ct));
                    Assert.Equal(1, downloader.Calls);
                    Assert.Null(source.TryGet(AdultFace));
                }

                return 0;
            },
            Ct);
    }

    [Fact]
    public async Task Trava_passageira_no_arquivo_nao_apaga_o_arquivo_bom_do_cache()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var cacheDirectory = Path.Combine(temp.Path, "imagens");
                var image = new VndbImageRef("https://t.vndb.org/ch/12/3412.jpg");
                var (first, _, _, _) = NewSource(temp.Path, cacheDirectory);
                using (first)
                {
                    (await first.LoadAsync(image, Ct))!.Dispose();
                }

                var file = Directory.EnumerateFiles(cacheDirectory).Single();
                var (source, _, downloader, _) = NewSource(temp.Path, cacheDirectory);
                using (source)
                {
                    // Outro processo (antivírus, CLI) segura o arquivo: abrir falha, o conteúdo não está corrompido.
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        Assert.Null(await source.LoadAsync(image, Ct));
                    }

                    Assert.True(File.Exists(file));

                    using var lease = await source.LoadAsync(image, Ct);
                    Assert.NotNull(lease);
                    Assert.Equal(0, downloader.Calls);
                }

                return 0;
            },
            Ct);
    }

    [Fact]
    public async Task Arquivo_com_a_assinatura_certa_que_nao_decodifica_sai_do_cache_e_espera_a_janela_de_nova_tentativa()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var cacheDirectory = Path.Combine(temp.Path, "imagens");
                var broken = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
                var good = false;
                var (source, _, downloader, time) = NewSource(temp.Path, cacheDirectory, _ => good ? Png() : broken);
                using (source)
                {
                    var image = new VndbImageRef("https://t.vndb.org/ch/12/3412.jpg");

                    Assert.Null(await source.LoadAsync(image, Ct));
                    Assert.Empty(Directory.EnumerateFiles(cacheDirectory));
                    Assert.Null(await source.LoadAsync(image, Ct));
                    Assert.Equal(1, downloader.Calls);

                    good = true;
                    time.Advance(VndbImageCache.RetryAfter + TimeSpan.FromSeconds(1));

                    using var lease = await source.LoadAsync(image, Ct);
                    Assert.NotNull(lease);
                    Assert.Equal(2, downloader.Calls);
                }

                return 0;
            },
            Ct);
    }

    [Fact]
    public async Task O_bitmap_so_e_descartado_quando_sai_da_memoria_e_o_ultimo_controle_o_devolve()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var (source, _, _, _) = NewSource(temp.Path, Path.Combine(temp.Path, "imagens"), capacity: 1);
                var first = new VndbImageRef("https://t.vndb.org/ch/12/1.jpg");
                var second = new VndbImageRef("https://t.vndb.org/ch/12/2.jpg");
                using (source)
                {
                    var onScreen = await source.LoadAsync(first, Ct);
                    Assert.NotNull(onScreen);
                    var evictor = await source.LoadAsync(second, Ct);
                    Assert.NotNull(evictor);

                    // O primeiro saiu da memória (capacidade 1), mas um controle ainda o mostra.
                    Assert.Null(source.TryGet(first));
                    Assert.Equal(16, onScreen.Bitmap.PixelSize.Width);

                    var firstBitmap = onScreen.Bitmap;
                    onScreen.Dispose();
                    onScreen.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => _ = firstBitmap.PixelSize);

                    var secondBitmap = evictor.Bitmap;
                    source.Dispose();
                    Assert.Equal(16, secondBitmap.PixelSize.Width);
                    evictor.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => _ = secondBitmap.PixelSize);
                }

                return 0;
            },
            Ct);
    }

    [Fact]
    public async Task Duas_cargas_da_mesma_imagem_dividem_um_bitmap_e_o_descartado_nao_afeta_ninguem()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var (source, _, downloader, _) = NewSource(temp.Path, Path.Combine(temp.Path, "imagens"));
                var image = new VndbImageRef("https://t.vndb.org/ch/12/3412.jpg");
                using (source)
                {
                    var leases = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => source.LoadAsync(image, Ct)));

                    Assert.Equal(1, downloader.Calls);
                    Assert.All(leases, l => Assert.Same(leases[0]!.Bitmap, l!.Bitmap));
                    Assert.Equal(16, leases[0]!.Bitmap.PixelSize.Width);
                    foreach (var lease in leases)
                    {
                        lease!.Dispose();
                    }
                }

                return 0;
            },
            Ct);
    }

    // ----- Controle reciclado -----

    private sealed class GatedSource : IVndbImageSource
    {
        private readonly Dictionary<string, TaskCompletionSource<BitmapLease?>> _pending = [];

        public event EventHandler? PrivacyChanged;

        public int Requests => _pending.Count;

        public void RaisePrivacyChanged() => PrivacyChanged?.Invoke(this, EventArgs.Empty);

        public void RevalidatePrivacy()
        {
        }

        public BitmapLease? TryGet(VndbImageRef image) => null;

        public Task<BitmapLease?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource<BitmapLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[image.Url] = gate;
            return gate.Task;
        }

        public void Complete(string url, BitmapLease lease) => _pending[url].SetResult(lease);
    }

    private static WriteableBitmap Blank() => new(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    [Fact]
    public async Task Trocar_a_imagem_com_uma_carga_em_andamento_ignora_a_carga_velha_e_devolve_a_posse()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var gated = new GatedSource();
                VndbImages.Source = gated;
                var a = new VndbImageRef("https://t.vndb.org/ch/12/a.jpg");
                var b = new VndbImageRef("https://t.vndb.org/ch/12/b.jpg");
                var portrait = new Portrait { Image = a, Seed = "A" };
                var window = new Window { Width = 100, Height = 100, Content = portrait };
                try
                {
                    window.Show();
                    ScreenHost.Settle(window);
                    portrait.Image = b;
                    ScreenHost.Settle(window);
                    Assert.Equal(2, gated.Requests);

                    // A carga velha (A) chega depois da troca: não aparece e o bitmap dela é devolvido.
                    var bitmapA = Blank();
                    gated.Complete(a.Url, BitmapLease.Own(bitmapA));
                    await WaitUntil(() =>
                    {
                        try
                        {
                            _ = bitmapA.PixelSize;
                            return false;
                        }
                        catch (ObjectDisposedException)
                        {
                            return true;
                        }
                    });
                    Assert.False(portrait.HasImage);

                    var bitmapB = Blank();
                    gated.Complete(b.Url, BitmapLease.Own(bitmapB));
                    await WaitUntil(() => portrait.HasImage, window);
                    Assert.Equal(8, bitmapB.PixelSize.Width);

                    // Sem imagem, o controle devolve a posse e volta ao marcador.
                    portrait.Image = null;
                    ScreenHost.Settle(window);
                    Assert.False(portrait.HasImage);
                    Assert.Throws<ObjectDisposedException>(() => _ = bitmapB.PixelSize);
                }
                finally
                {
                    window.Close();
                    VndbImages.Source = null;
                }

                return 0;
            },
            Ct);
    }
}
