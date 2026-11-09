using System.Buffers.Binary;
using System.Text.Json;

namespace VNForge.Patching;

/// <summary>
/// O arquivo <c>.vnfpatch</c>: <c>VNFPATCH</c> (8 bytes), versão do formato (uint32), tamanho do manifesto (int32), o manifesto em JSON UTF-8 e os deltas
/// um atrás do outro. Nada ali é arquivo inteiro do jogo: cada delta é a diferença para o original (<see cref="DeltaCodec"/>).
/// </summary>
public sealed class PatchPackage : IDisposable
{
    internal static ReadOnlySpan<byte> Magic => "VNFPATCH"u8;

    internal const int FormatVersion = 1;

    private const int MaxManifestBytes = 16 * 1024 * 1024;

    private readonly Stream _stream;

    private PatchPackage(Stream stream, PatchManifest manifest, long payloadStart)
    {
        _stream = stream;
        Manifest = manifest;
        PayloadStart = payloadStart;
    }

    public PatchManifest Manifest { get; }

    /// <summary>Posição, no pacote, onde o primeiro delta começa.</summary>
    public long PayloadStart { get; }

    /// <summary>Abre um pacote já pronto. O pacote passa a ser dono do <paramref name="stream"/>, que precisa permitir busca.</summary>
    /// <exception cref="PatchException">O arquivo não é um patch do VNForge, ou é de um formato que este instalador não conhece.</exception>
    public static PatchPackage Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            Span<byte> header = stackalloc byte[16];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || !header[..8].SequenceEqual(Magic))
            {
                throw new PatchException("Este arquivo não é um patch do VNForge.");
            }

            var version = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
            if (version != FormatVersion)
            {
                throw new PatchException($"Este patch é do formato {version}, e este instalador só entende o formato {FormatVersion}. Baixe o instalador de novo.");
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);
            if (length is <= 0 or > MaxManifestBytes)
            {
                throw new PatchException("O patch está corrompido (manifesto inválido).");
            }

            var json = new byte[length];
            stream.ReadExactly(json);
            var manifest = JsonSerializer.Deserialize<PatchManifest>(json, PatchManifest.Json)
                ?? throw new PatchException("O patch está corrompido (manifesto vazio).");
            Validate(manifest);
            return new PatchPackage(stream, manifest, 16 + length);
        }
        catch (Exception ex) when (ex is JsonException or EndOfStreamException)
        {
            stream.Dispose();
            throw new PatchException("O patch está corrompido (manifesto ilegível).", ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Escreve o cabeçalho, o manifesto e os deltas.</summary>
    internal static void Write(Stream output, PatchManifest manifest, Stream payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, PatchManifest.Json);
        Span<byte> header = stackalloc byte[16];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..16], json.Length);
        output.Write(header);
        output.Write(json);
        payload.Position = 0;
        payload.CopyTo(output);
    }

    /// <summary>Entrega o arquivo como ele fica depois do patch, ou o delta decodificado sobre <paramref name="original"/>, em <paramref name="output"/>.</summary>
    internal void Apply(PatchFileEntry entry, Stream? original, Stream output)
    {
        var delta = ReadDelta(entry);
        if (entry.Created)
        {
            DeltaCodec.DecodeNew(delta, output);
            return;
        }

        if (original is null)
        {
            throw new PatchException($"Falta o arquivo original de {entry.Path}.");
        }

        DeltaCodec.Decode(original, delta, output);
    }

    private MemoryStream ReadDelta(PatchFileEntry entry)
    {
        var bytes = new byte[entry.Length];
        _stream.Seek(PayloadStart + entry.Offset, SeekOrigin.Begin);
        _stream.ReadExactly(bytes);
        return new MemoryStream(bytes);
    }

    private static void Validate(PatchManifest manifest)
    {
        if (manifest.Files is null || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files.Count == 0)
        {
            throw new PatchException("O patch está corrompido (sem arquivos).");
        }

        foreach (var file in manifest.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.ResultSha256) || file.Length < 0 || file.Offset < 0)
            {
                throw new PatchException("O patch está corrompido (entrada inválida).");
            }

            GamePaths.Resolve(Path.GetTempPath(), file.Path);
        }
    }

    public void Dispose() => _stream.Dispose();

}
