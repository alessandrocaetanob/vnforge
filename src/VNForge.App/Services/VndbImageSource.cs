using Avalonia.Media.Imaging;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Services;

/// <summary>De onde os controles <c>Portrait</c> e <c>Cover</c> tiram o <see cref="Bitmap"/> de uma imagem do VNDB. Atrás de uma interface para os testes trocarem por uma fonte sem rede.</summary>
public interface IVndbImageSource
{
    /// <summary>O bitmap se já está na memória e a privacidade deixa ver; nulo senão. Não toca o disco nem a rede, então serve para a thread de UI.</summary>
    Bitmap? TryGet(VndbImageRef image);

    /// <summary>O bitmap da imagem (do cache em disco ou baixada); nulo quando a privacidade a esconde ou ela não pôde ser obtida. Sem trabalho na thread do chamador além de iniciar a tarefa.</summary>
    Task<Bitmap?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken);
}

/// <summary>A fonte do app: o <see cref="VndbImageCache"/> em disco e uma memória pequena de bitmaps já decodificados.</summary>
public sealed class VndbImageSource(VndbImageCache cache, int capacity = 160) : IVndbImageSource, IDisposable
{
    private readonly Dictionary<string, Bitmap> _memory = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly Lock _gate = new();

    public Bitmap? TryGet(VndbImageRef image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!cache.IsVisible(image))
        {
            return null;
        }

        lock (_gate)
        {
            return _memory.GetValueOrDefault(image.Url);
        }
    }

    /// <summary>Libera o <see cref="VndbImageCache"/> que a fonte recebeu: ela é a dona dele.</summary>
    public void Dispose() => cache.Dispose();

    public Task<Bitmap?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Task.Run(() => LoadCoreAsync(image, cancellationToken), cancellationToken);
    }

    private async Task<Bitmap?> LoadCoreAsync(VndbImageRef image, CancellationToken cancellationToken)
    {
        if (TryGet(image) is { } known)
        {
            return known;
        }

        if (await cache.GetAsync(image, cancellationToken).ConfigureAwait(false) is not { } path)
        {
            return null;
        }

        var bitmap = Decode(path);
        if (bitmap is not null)
        {
            Remember(image.Url, bitmap);
        }

        return bitmap;
    }

    private static Bitmap? Decode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void Remember(string url, Bitmap bitmap)
    {
        lock (_gate)
        {
            if (_memory.TryAdd(url, bitmap))
            {
                _order.Enqueue(url);
            }

            // O bitmap que sai da memória segue vivo enquanto algum controle o usa; o coletor o libera depois.
            while (_order.Count > capacity && _order.TryDequeue(out var oldest))
            {
                _memory.Remove(oldest);
            }
        }
    }
}

/// <summary>
/// A privacidade da Biblioteca (<c>library.json</c>, na pasta de configuração) para o cache de imagens. A primeira leitura é na hora; depois vale o último valor
/// e, passado <see cref="Staleness"/>, uma leitura em segundo plano o atualiza, sem segurar a tela. Arquivo ausente ou ilegível vale a privacidade padrão, que esconde o conteúdo adulto.
/// </summary>
public sealed class LibraryPrivacyReader(string configDirectory, TimeProvider? time = null)
{
    public static readonly TimeSpan Staleness = TimeSpan.FromSeconds(2);

    private readonly LibraryFile _file = new(configDirectory);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private LibraryPrivacy _current = new();
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _refreshing;

    public LibraryPrivacy Current()
    {
        bool cold;
        bool refresh;
        lock (_gate)
        {
            cold = _readAt == DateTimeOffset.MinValue;
            refresh = !cold && !_refreshing && _time.GetUtcNow() - _readAt >= Staleness;
            if (refresh)
            {
                _refreshing = true;
            }
        }

        if (cold)
        {
            Reload();
        }
        else if (refresh)
        {
            _ = Task.Run(Reload);
        }

        lock (_gate)
        {
            return _current;
        }
    }

    private void Reload()
    {
        LibraryPrivacy privacy;
        try
        {
            privacy = _file.Load().Privacy;
        }
        catch (Exception e) when (e is LibraryException or IOException or UnauthorizedAccessException)
        {
            privacy = new LibraryPrivacy();
        }

        lock (_gate)
        {
            _current = privacy;
            _readAt = _time.GetUtcNow();
            _refreshing = false;
        }
    }
}

/// <summary>A fonte de imagens do processo. O app a monta na partida (<see cref="CreateDefault"/>); sem fonte os controles ficam com o marcador.</summary>
public static class VndbImages
{
    /// <summary>A fonte que <c>Portrait</c> e <c>Cover</c> usam; nula até o app montá-la.</summary>
    public static IVndbImageSource? Source { get; set; }

    /// <summary>
    /// O cache em <c>%LOCALAPPDATA%/VNForge/cache/vndb-images</c> com o downloader HTTP e a privacidade da Biblioteca de <paramref name="configDirectory"/>.
    /// O cache vive até o fim do processo, como a fonte.
    /// </summary>
    public static IVndbImageSource CreateDefault(string configDirectory) =>
        new VndbImageSource(new VndbImageCache(VndbImageCache.DefaultDirectory(), HttpImageDownloader.Create(), new LibraryPrivacyReader(configDirectory).Current));
}
