using Avalonia.Media.Imaging;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Services;

/// <summary>
/// Uma posse de um <see cref="Avalonia.Media.Imaging.Bitmap"/> compartilhado: a fonte de imagens o guarda na memória e cada controle que o mostra tem a sua posse.
/// O bitmap só é descartado quando a última posse (a da memória inclusive) é devolvida, então nenhum controle fica com uma imagem descartada na tela.
/// </summary>
public sealed class BitmapLease : IDisposable
{
    private readonly Shared _shared;
    private int _released;

    private BitmapLease(Shared shared) => _shared = shared;

    /// <summary>O bitmap; vale até <see cref="Dispose"/>.</summary>
    public Bitmap Bitmap => _shared.Bitmap;

    /// <summary>Uma posse só de <paramref name="bitmap"/>: ele é descartado quando ela é devolvida. Serve a quem não divide o bitmap com ninguém.</summary>
    public static BitmapLease Own(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return new Shared(bitmap).Lease();
    }

    /// <summary>Devolve a posse; chamar de novo não faz nada.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _shared.Release();
        }
    }

    /// <summary>O bitmap e a conta de quem o segura.</summary>
    internal sealed class Shared(Bitmap bitmap)
    {
        private int _holders;

        public Bitmap Bitmap { get; } = bitmap;

        public BitmapLease Lease()
        {
            Hold();
            return new BitmapLease(this);
        }

        public void Hold() => Interlocked.Increment(ref _holders);

        public void Release()
        {
            if (Interlocked.Decrement(ref _holders) == 0)
            {
                Bitmap.Dispose();
            }
        }
    }
}

/// <summary>De onde os controles <c>Portrait</c> e <c>Cover</c> tiram o <see cref="Bitmap"/> de uma imagem do VNDB. Atrás de uma interface para os testes trocarem por uma fonte sem rede.</summary>
public interface IVndbImageSource
{
    /// <summary>A privacidade da Biblioteca mudou (o ajuste foi trocado no app ou na CLI). Pode vir de qualquer thread; os controles na tela releem a imagem.</summary>
    event EventHandler? PrivacyChanged;

    /// <summary>A posse do bitmap se ele já está na memória e a privacidade deixa ver; nulo senão. Não toca o disco nem a rede, então serve para a thread de UI. Quem recebe devolve a posse (<see cref="BitmapLease.Dispose"/>).</summary>
    BitmapLease? TryGet(VndbImageRef image);

    /// <summary>
    /// Confere em segundo plano se a privacidade mudou por fora; se mudou, <see cref="PrivacyChanged"/> dispara. Quem mostra uma imagem que veio da memória chama-o, porque
    /// o acerto na memória não passa por <see cref="LoadAsync"/>. Várias chamadas seguidas viram uma.
    /// </summary>
    void RevalidatePrivacy();

    /// <summary>
    /// A posse do bitmap da imagem (do cache em disco ou baixada); nula quando a privacidade a esconde ou ela não pôde ser obtida. Relê a privacidade antes de decidir, fora da thread do chamador.
    /// </summary>
    Task<BitmapLease?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken);
}

/// <summary>
/// A fonte do app: o <see cref="VndbImageCache"/> em disco e uma memória pequena de bitmaps já decodificados. É dona do cache e do leitor de privacidade e os libera no <see cref="Dispose"/>;
/// os bitmaps que saem da memória só são descartados quando os controles que os mostram os devolvem.
/// </summary>
public sealed class VndbImageSource : IVndbImageSource, IDisposable
{
    private readonly VndbImageCache _cache;
    private readonly LibraryPrivacyReader? _privacy;
    private readonly int _capacity;
    private readonly Dictionary<string, BitmapLease.Shared> _memory = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly Lock _gate = new();
    private bool _disposed;
    private int _revalidating;

    /// <param name="cache">O cache em disco.</param>
    /// <param name="privacy">O leitor da privacidade que o <paramref name="cache"/> consulta; a fonte o atualiza a cada <see cref="LoadAsync"/> e repassa o <see cref="PrivacyChanged"/> dele.</param>
    /// <param name="capacity">Quantos bitmaps ficam na memória.</param>
    public VndbImageSource(VndbImageCache cache, LibraryPrivacyReader? privacy = null, int capacity = 160)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _privacy = privacy;
        _capacity = capacity;
        if (privacy is not null)
        {
            privacy.Changed += OnPrivacyChanged;
        }
    }

    public event EventHandler? PrivacyChanged;

    public BitmapLease? TryGet(VndbImageRef image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!_cache.IsVisible(image))
        {
            return null;
        }

        lock (_gate)
        {
            return _memory.TryGetValue(image.Url, out var shared) ? shared.Lease() : null;
        }
    }

    public Task<BitmapLease?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Task.Run(() => LoadCoreAsync(image, cancellationToken), cancellationToken);
    }

    public void RevalidatePrivacy()
    {
        if (_privacy is null || Interlocked.Exchange(ref _revalidating, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _privacy.Refresh();
            }
            catch (ObjectDisposedException)
            {
                // A fonte foi liberada enquanto a conferência esperava na fila.
            }
            finally
            {
                Volatile.Write(ref _revalidating, 0);
            }
        });
    }

    public void Dispose()
    {
        if (_privacy is not null)
        {
            _privacy.Changed -= OnPrivacyChanged;
            _privacy.Dispose();
        }

        lock (_gate)
        {
            _disposed = true;
            foreach (var shared in _memory.Values)
            {
                shared.Release();
            }

            _memory.Clear();
            _order.Clear();
        }

        _cache.Dispose();
    }

    private void OnPrivacyChanged(object? sender, EventArgs e) => PrivacyChanged?.Invoke(this, EventArgs.Empty);

    private async Task<BitmapLease?> LoadCoreAsync(VndbImageRef image, CancellationToken cancellationToken)
    {
        // A decisão de mostrar usa a privacidade de agora: se o ajuste mudou por fora (a CLI), esta leitura já a vê.
        _privacy?.Refresh();
        var lease = await ObtainAsync(image, cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return null;
        }

        // Download e decodificação levam tempo: o ajuste pode ter sido desligado nesse intervalo, e a posse não sai daqui com uma imagem que a privacidade já esconde.
        _privacy?.Refresh();
        if (_cache.IsVisible(image))
        {
            return lease;
        }

        lease.Dispose();
        return null;
    }

    private async Task<BitmapLease?> ObtainAsync(VndbImageRef image, CancellationToken cancellationToken)
    {
        if (TryGet(image) is { } known)
        {
            return known;
        }

        if (await _cache.GetAsync(image, cancellationToken).ConfigureAwait(false) is not { } path)
        {
            return null;
        }

        var (bitmap, corrupt) = Decode(path);
        if (bitmap is null)
        {
            if (corrupt)
            {
                // Os bytes tinham cara de imagem e não decodificam: o arquivo sai do disco e a próxima tentativa espera a janela do cache.
                _cache.Reject(image);
            }

            return null;
        }

        return Remember(image.Url, bitmap);
    }

    /// <summary>
    /// Decodifica o arquivo. <c>Corrupt</c> só é verdadeiro quando o conteúdo é que não decodifica; falha ao abrir ou ler o arquivo (uma trava passageira do antivírus ou de outro
    /// processo) devolve <c>(null, false)</c> e o arquivo bom fica no cache para a próxima vez.
    /// </summary>
    private static (Bitmap? Bitmap, bool Corrupt) Decode(string path)
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, false);
        }

        using (stream)
        {
            try
            {
                return (new Bitmap(stream), false);
            }
            catch (IOException)
            {
                return (null, false);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return (null, true);
            }
        }
    }

    private BitmapLease Remember(string url, Bitmap bitmap)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return BitmapLease.Own(bitmap);
            }

            if (_memory.TryGetValue(url, out var winner))
            {
                // Dois controles pediram a mesma imagem ao mesmo tempo: fica o primeiro bitmap e este é descartado.
                bitmap.Dispose();
                return winner.Lease();
            }

            var shared = new BitmapLease.Shared(bitmap);
            shared.Hold();
            _memory[url] = shared;
            _order.Enqueue(url);
            var lease = shared.Lease();
            while (_order.Count > _capacity && _order.TryDequeue(out var oldest))
            {
                if (_memory.Remove(oldest, out var evicted))
                {
                    evicted.Release();
                }
            }

            return lease;
        }
    }
}

/// <summary>
/// A privacidade da Biblioteca (<c>library.json</c>, na pasta de configuração) para o cache de imagens. <see cref="Current"/> devolve o último valor lido, da memória, sem tocar o disco
/// (serve para a thread de UI); até a primeira leitura vale a privacidade padrão, que esconde o conteúdo adulto. <see cref="Refresh"/> lê o arquivo de verdade e deve rodar fora da UI:
/// quem decide mostrar uma imagem chama-o antes. Um observador do arquivo também chama <see cref="Refresh"/> quando o <c>library.json</c> muda (a CLI trocou o ajuste com o app aberto) e
/// <see cref="Changed"/> avisa os controles. Arquivo ausente ou ilegível vale a privacidade padrão.
/// </summary>
public sealed class LibraryPrivacyReader : IDisposable
{
    private readonly LibraryFile _file;
    private readonly Lock _read = new();
    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly Lock _watching = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;
    private LibraryPrivacy _current = new();
    private (long Ticks, long Length)? _stamp;

    /// <param name="configDirectory">A pasta de configuração, onde está o <c>library.json</c>.</param>
    /// <param name="watch">Observar o arquivo para atualizar sozinho; os testes de leitura desligam.</param>
    public LibraryPrivacyReader(string configDirectory, bool watch = true)
    {
        _file = new LibraryFile(configDirectory);
        _directory = configDirectory;
        if (watch)
        {
            StartWatching();
        }
    }

    /// <summary>A privacidade mudou de valor. Vem da thread em que <see cref="Refresh"/> rodou.</summary>
    public event EventHandler? Changed;

    /// <summary>O último valor lido, da memória.</summary>
    public LibraryPrivacy Current()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    /// <summary>
    /// Relê o <c>library.json</c> se ele mudou desde a última leitura (data e tamanho) e devolve a privacidade de agora; avisa <see cref="Changed"/> quando o valor mudou.
    /// Toca o disco: não chame na thread de UI.
    /// </summary>
    public LibraryPrivacy Refresh()
    {
        LibraryPrivacy before;
        LibraryPrivacy after;
        lock (_read)
        {
            lock (_gate)
            {
                before = _current;
            }

            var stamp = Stamp();
            if (stamp is not null && stamp == _stamp)
            {
                return before;
            }

            var read = Read();
            _stamp = read.Ok ? stamp : null;
            after = read.Privacy;
            lock (_gate)
            {
                _current = after;
            }
        }

        if (after != before)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return after;
    }

    public void Dispose()
    {
        lock (_watching)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>
    /// Cria o observador do <c>library.json</c>. A pasta é criada antes: sem ela (o app abriu antes de a Biblioteca existir) o observador nunca nasceria e uma mudança feita
    /// pela CLI não chegaria. Se o observador falhar (buffer cheio, pasta trocada), é recriado e o arquivo é relido.
    /// </summary>
    private void StartWatching()
    {
        lock (_watching)
        {
            if (_disposed)
            {
                return;
            }

            _watcher?.Dispose();
            _watcher = null;
            try
            {
                Directory.CreateDirectory(_directory);
                var watcher = new FileSystemWatcher(_directory, LibraryFile.FileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                watcher.Changed += OnFileEvent;
                watcher.Created += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Renamed += OnFileEvent;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
            {
                // Sem observador, a conferência a cada imagem mostrada (RevalidatePrivacy) ainda pega a mudança.
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        _ = Task.Run(() =>
        {
            StartWatching();
            try
            {
                Refresh();
            }
            catch (ObjectDisposedException)
            {
                // O leitor foi liberado enquanto o evento esperava na fila.
            }
        });

    private (long, long)? Stamp()
    {
        try
        {
            var info = new FileInfo(_file.Path);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : (-1, -1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private (LibraryPrivacy Privacy, bool Ok) Read()
    {
        try
        {
            return (_file.Load().Privacy, true);
        }
        catch (Exception e) when (e is LibraryException or IOException or UnauthorizedAccessException)
        {
            return (new LibraryPrivacy(), false);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _ = Task.Run(() =>
        {
            try
            {
                Refresh();
            }
            catch (ObjectDisposedException)
            {
                // O leitor foi liberado enquanto o evento esperava na fila.
            }
        });
}

/// <summary>A fonte de imagens do processo. O app a monta na partida (<see cref="CreateDefault"/>); sem fonte os controles ficam com o marcador.</summary>
public static class VndbImages
{
    /// <summary>A fonte que <c>Portrait</c> e <c>Cover</c> usam; nula até o app montá-la.</summary>
    public static IVndbImageSource? Source { get; set; }

    /// <summary>
    /// O cache em <c>%LOCALAPPDATA%/VNForge/cache/vndb-images</c> com o downloader HTTP e a privacidade da Biblioteca de <paramref name="configDirectory"/>.
    /// A primeira leitura do <c>library.json</c> vai para o segundo plano: até ela terminar vale a privacidade padrão, que esconde o conteúdo adulto.
    /// </summary>
    public static IVndbImageSource CreateDefault(string configDirectory)
    {
        var privacy = new LibraryPrivacyReader(configDirectory);
        _ = Task.Run(privacy.Refresh);
        return new VndbImageSource(new VndbImageCache(VndbImageCache.DefaultDirectory(), HttpImageDownloader.Create(), privacy.Current), privacy);
    }
}
