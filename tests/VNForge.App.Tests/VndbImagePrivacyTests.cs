using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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
[Trait("Risco", "R9")]
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
}
