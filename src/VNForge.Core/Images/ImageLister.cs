using System.Security.Cryptography;
using VNForge.Core.Engines;

namespace VNForge.Core.Images;

/// <summary>O que separa uma imagem que vale mandar ao modelo do resto.</summary>
public sealed record ImageScanOptions
{
    /// <summary>Menos pixels que isto (largura vezes altura) é ícone ou enfeite, sem texto que valha a chamada. Padrão: 2048, algo como 64 por 32.</summary>
    public int MinPixels { get; init; } = 2048;

    /// <summary>A API do modelo recusa lado acima de 8000 pixels.</summary>
    public int MaxSide { get; init; } = 8000;

    /// <summary>
    /// A API direta recusa imagem acima de 10 MB em base64 (5 MB no Bedrock e no Google Cloud; platform.claude.com/docs/en/build-with-claude/vision, "Request limits").
    /// A base64 incha o arquivo em 4/3, então o arquivo vai até 7,5 MB; fica em 7,4 MB para não cair na fronteira exata.
    /// </summary>
    public long MaxBytes { get; init; } = 7_400_000;
}

/// <summary>Uma imagem que passou no filtro e pode ir ao modelo.</summary>
/// <param name="Archive">Pacote do jogo, relativo à pasta do jogo e com <c>/</c>.</param>
/// <param name="Path">Caminho da imagem dentro do pacote.</param>
/// <param name="Size">Tamanho do arquivo descomprimido, em bytes.</param>
/// <param name="Hash">SHA-256 do conteúdo, em hexadecimal minúsculo.</param>
public sealed record ImageCandidate(string Archive, string Path, ImageInfo Info, long Size, string Hash);

public enum ImageSkipReason
{
    /// <summary>Menos pixels que <see cref="ImageScanOptions.MinPixels"/>.</summary>
    TooSmall,

    /// <summary>Acima de <see cref="ImageScanOptions.MaxBytes"/> ou <see cref="ImageScanOptions.MaxSide"/>.</summary>
    TooLarge,

    /// <summary>Formato que a API não lê: BMP, ou um formato do jogo (como YCG) que o VNForge não sabe decodificar.</summary>
    UnsupportedFormat,

    /// <summary>Tem nome de imagem, mas os bytes não são uma imagem inteira de um formato conhecido.</summary>
    NotDecodable,

    /// <summary>O pacote não entregou o conteúdo (hash ou tamanho divergente).</summary>
    Unreadable,
}

/// <summary>Uma entrada de imagem que ficou fora do inventário, com o motivo.</summary>
public sealed record SkippedImage(string Archive, string Path, ImageSkipReason Reason);

/// <summary>O levantamento das imagens do jogo.</summary>
/// <param name="ArchivesRead">Pacotes abertos com sucesso.</param>
/// <param name="Warnings">Pacotes que não puderam ser lidos e outros avisos, em português.</param>
public sealed record ImageListing(
    IReadOnlyList<ImageCandidate> Candidates,
    IReadOnlyList<SkippedImage> Skipped,
    IReadOnlyList<string> Warnings,
    int ArchivesRead);

/// <summary>
/// Lista as imagens dos pacotes de um jogo, só lendo (regra 2 do <c>CLAUDE.md</c>): abre cada pacote que um dos formatos reconhece, lê o cabeçalho de
/// cada imagem e separa as que o modelo pode ler das que não. Não chama a IA.
/// </summary>
public static class ImageLister
{
    private static readonly string[] ReadableExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    // Formatos de imagem de jogo que não decodificamos; entram na contagem de ignoradas sem serem lidos.
    private static readonly string[] OtherImageExtensions = [".ycg", ".tga", ".dds", ".psd", ".tif", ".tiff", ".ico", ".jp2", ".pcx"];

    /// <exception cref="VNForgeException">A pasta do jogo não existe.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado; vale entre um pacote e outro e entre uma imagem e outra.</exception>
    public static ImageListing List(
        string gameDirectory, IReadOnlyList<IArchiveFormat> formats, ImageScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameDirectory);
        ArgumentNullException.ThrowIfNull(formats);
        options ??= new ImageScanOptions();
        if (!Directory.Exists(gameDirectory))
        {
            throw new VNForgeException($"A pasta do jogo não existe: {gameDirectory}");
        }

        List<string> warnings = [];
        List<ImageCandidate> candidates = [];
        List<SkippedImage> skipped = [];
        var archivesRead = 0;
        foreach (var file in ArchiveOpener.Find(gameDirectory, formats, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = ArchiveOpener.RelativeName(gameDirectory, file);
            IArchive archive;
            try
            {
                archive = ArchiveOpener.Open(file, formats);
            }
            catch (Exception ex) when (ArchiveOpener.IsReadFailure(ex))
            {
                warnings.Add($"{relative}: {ex.Message}");
                continue;
            }

            archivesRead++;
            using (archive)
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Classify(archive, entry, relative, options, candidates, skipped);
                }
            }
        }

        return new ImageListing(candidates, skipped, warnings, archivesRead);
    }

    private static void Classify(
        IArchive archive, ArchiveEntry entry, string archiveName, ImageScanOptions options, List<ImageCandidate> candidates, List<SkippedImage> skipped)
    {
        var extension = System.IO.Path.GetExtension(entry.Path);
        if (OtherImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            skipped.Add(new SkippedImage(archiveName, entry.Path, ImageSkipReason.UnsupportedFormat));
            return;
        }

        if (!ReadableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (entry.Size > options.MaxBytes)
        {
            skipped.Add(new SkippedImage(archiveName, entry.Path, ImageSkipReason.TooLarge));
            return;
        }

        byte[] data;
        try
        {
            data = ImageBytes.Read(archive, entry);
        }
        catch (Exception ex) when (ArchiveOpener.IsReadFailure(ex))
        {
            skipped.Add(new SkippedImage(archiveName, entry.Path, ImageSkipReason.Unreadable));
            return;
        }

        var info = ImageProbe.Read(data);
        if (info is null)
        {
            skipped.Add(new SkippedImage(archiveName, entry.Path, ImageSkipReason.NotDecodable));
        }
        else if (SkipReason(info, data.Length, options) is { } reason)
        {
            skipped.Add(new SkippedImage(archiveName, entry.Path, reason));
        }
        else
        {
            candidates.Add(new ImageCandidate(archiveName, entry.Path, info, data.Length, Convert.ToHexStringLower(SHA256.HashData(data))));
        }
    }

    private static ImageSkipReason? SkipReason(ImageInfo info, int bytes, ImageScanOptions options)
    {
        if (info.MediaType is null)
        {
            return ImageSkipReason.UnsupportedFormat;
        }

        if (info.Pixels < options.MinPixels)
        {
            return ImageSkipReason.TooSmall;
        }

        return info.Width > options.MaxSide || info.Height > options.MaxSide || bytes > options.MaxBytes ? ImageSkipReason.TooLarge : null;
    }
}

internal static class ImageBytes
{
    public static byte[] Read(IArchive archive, ArchiveEntry entry)
    {
        using var stream = archive.OpenEntry(entry);
        using var buffer = new MemoryStream(checked((int)Math.Min(entry.Size, int.MaxValue)));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>Achar e abrir os pacotes de uma pasta de jogo, para o <see cref="ImageLister"/> e o <see cref="ImageTextScanner"/>.</summary>
internal static class ArchiveOpener
{
    public static IEnumerable<string> Find(string gameDirectory, IReadOnlyList<IArchiveFormat> formats, List<string> warnings)
    {
        var extensions = formats.SelectMany(f => f.FileExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(gameDirectory, "*", options).Where(f => extensions.Contains(System.IO.Path.GetExtension(f)))];
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            warnings.Add($"Não foi possível listar a pasta do jogo: {ex.Message}");
            return [];
        }

        return files.OrderBy(f => RelativeName(gameDirectory, f), StringComparer.OrdinalIgnoreCase);
    }

    public static string RelativeName(string gameDirectory, string file) =>
        System.IO.Path.GetRelativePath(gameDirectory, file).Replace(System.IO.Path.DirectorySeparatorChar, '/');

    public static IArchive Open(string file, IReadOnlyList<IArchiveFormat> formats)
    {
        var extension = System.IO.Path.GetExtension(file);
        var stream = File.OpenRead(file);
        try
        {
            var format = formats.FirstOrDefault(f => f.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && f.CanOpen(stream))
                ?? throw new VNForgeException("o formato do pacote não é reconhecido.");
            return format.Open(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static bool IsReadFailure(Exception ex) =>
        ex is VNForgeException or IOException or InvalidDataException or UnauthorizedAccessException;
}
