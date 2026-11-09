using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Library;

namespace VNForge.Core.Vndb;

/// <summary>Baixa os bytes de uma imagem. O cliente do VNDB implementa em <c>VNForge.Vndb</c>; os testes usam um falso, sem rede.</summary>
public interface IImageDownloader
{
    /// <summary>Os bytes da imagem, ou nulo quando o servidor não respondeu direito (rede fora, erro HTTP, tamanho acima do limite). Cancelar lança <see cref="OperationCanceledException"/>.</summary>
    Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// As capas e os retratos do VNDB em disco. Cada imagem é baixada uma vez (pelo <see cref="IImageDownloader"/>) e fica numa pasta do usuário, fora do projeto e da pasta do jogo.
/// A privacidade da Biblioteca decide antes de tudo: imagem que ela esconde não é baixada nem devolvida, mesmo que já esteja no disco. Só se guarda o que tem cara de imagem
/// (JPEG, PNG ou WebP, até <see cref="MaxBytes"/>) e só se baixa de <c>vndb.org</c> (<see cref="VndbImageVisibility.IsAllowedSource"/>).
/// </summary>
public sealed class VndbImageCache : IDisposable
{
    /// <summary>O maior arquivo que o cache guarda: as capas do VNDB passam pouco de 1 MB.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Quanto esperar antes de tentar de novo uma imagem que falhou.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private static readonly string[] Extensions = [".jpg", ".png", ".webp"];

    private readonly string _directory;
    private readonly IImageDownloader _downloader;
    private readonly Func<LibraryPrivacy> _privacy;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedAt = new(StringComparer.Ordinal);

    /// <param name="directory">A pasta das imagens; <see cref="DefaultDirectory"/> é a do app e da CLI.</param>
    /// <param name="downloader">Quem baixa.</param>
    /// <param name="privacy">A privacidade da Biblioteca, lida a cada pedido (trocar o ajuste vale na hora). Sem ela vale <see cref="LibraryPrivacy"/> padrão, que esconde o conteúdo adulto.</param>
    /// <param name="time">O relógio da espera entre tentativas; o do sistema por padrão.</param>
    /// <param name="maxConcurrentDownloads">Quantas imagens baixam ao mesmo tempo.</param>
    public VndbImageCache(string directory, IImageDownloader downloader, Func<LibraryPrivacy>? privacy = null, TimeProvider? time = null, int maxConcurrentDownloads = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentDownloads, 1);
        _directory = directory;
        _downloader = downloader;
        _privacy = privacy ?? (() => new LibraryPrivacy());
        _time = time ?? TimeProvider.System;
        _slots = new SemaphoreSlim(maxConcurrentDownloads);
    }

    /// <summary>A pasta de cache do usuário: <c>%LOCALAPPDATA%/VNForge/cache/vndb-images</c>, ou a temporária se o sistema não informar uma absoluta.</summary>
    public static string DefaultDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = !string.IsNullOrWhiteSpace(local) && Path.IsPathRooted(local) ? local : Path.GetTempPath();
        return Path.Combine(baseDirectory, "VNForge", "cache", "vndb-images");
    }

    /// <summary>O arquivo da imagem se ela já está no disco e a privacidade deixa ver; nulo senão. Não usa a rede.</summary>
    public string? Find(VndbImageRef image)
    {
        return IsVisible(image) ? Cached(image.Url) : null;
    }

    /// <summary>
    /// O arquivo da imagem, baixando-a se faltar. Nulo quando a privacidade a esconde (sem baixar), o endereço não é do VNDB, o download falhou ou o conteúdo não é uma imagem;
    /// a falha não é repetida por <see cref="RetryAfter"/>. Vários pedidos da mesma imagem ao mesmo tempo viram um download.
    /// </summary>
    /// <exception cref="OperationCanceledException">O pedido foi cancelado.</exception>
    public async Task<string?> GetAsync(VndbImageRef image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsVisible(image))
        {
            return null;
        }

        if (Cached(image.Url) is { } hit)
        {
            return hit;
        }

        if (RecentlyFailed(image.Url))
        {
            return null;
        }

        var gate = _locks.GetOrAdd(image.Url, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Quem esperou na fila vê o que o primeiro baixou, e a privacidade pode ter mudado nesse meio-tempo.
            if (!IsVisible(image))
            {
                return null;
            }

            if (Cached(image.Url) is { } done)
            {
                return done;
            }

            if (RecentlyFailed(image.Url))
            {
                return null;
            }

            return await DownloadAsync(image.Url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        _slots.Dispose();
        foreach (var gate in _locks.Values)
        {
            gate.Dispose();
        }
    }

    private async Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        byte[]? bytes;
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bytes = await _downloader.DownloadAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }

        if (bytes is null || bytes.Length > MaxBytes || ExtensionFor(bytes) is not { } extension)
        {
            _failedAt[url] = _time.GetUtcNow();
            return null;
        }

        var path = PathFor(url, extension);
        try
        {
            Directory.CreateDirectory(_directory);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _failedAt[url] = _time.GetUtcNow();
            return null;
        }

        _failedAt.TryRemove(url, out _);
        return path;
    }

    /// <summary>A imagem pode aparecer agora: o endereço é do VNDB e a privacidade da Biblioteca não a esconde. Lê a privacidade a cada chamada.</summary>
    public bool IsVisible(VndbImageRef image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return VndbImageVisibility.IsAllowedSource(image.Url) && !VndbImageVisibility.IsHidden(image, _privacy());
    }

    private bool RecentlyFailed(string url) =>
        _failedAt.TryGetValue(url, out var failed) && _time.GetUtcNow() - failed < RetryAfter;

    private string? Cached(string url) =>
        Extensions.Select(extension => PathFor(url, extension)).FirstOrDefault(File.Exists);

    private string PathFor(string url, string extension) =>
        Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + extension);

    private static string? ExtensionFor(byte[] bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => ".png",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ".webp",
        _ => null,
    };
}
