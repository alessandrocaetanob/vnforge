using System.IO.Compression;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VNForge.Patching;

/// <summary>
/// Diferença binária entre dois arquivos no formato VCDIFF (RFC 3284), com a biblioteca VCDiff (Apache-2.0; ver THIRD-PARTY-NOTICES.md). A biblioteca fica só aqui:
/// trocá-la mexe neste arquivo e na versão do formato do pacote. Cada delta leva o Adler-32 de cada janela e sai comprimido em Brotli.
/// </summary>
internal static class DeltaCodec
{
    /// <summary>Arquivo temporário que some ao fechar: os deltas de um .ypf de 200 MB não cabem em <see cref="MemoryStream"/> sem estourar a memória do tradutor.</summary>
    public static FileStream CreateTemp() =>
        new(Path.Combine(Path.GetTempPath(), "vnf-" + Guid.NewGuid().ToString("N") + ".tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);

    /// <summary>Escreve em <paramref name="output"/> o delta comprimido que transforma <paramref name="original"/> em <paramref name="result"/>.</summary>
    public static void Encode(Stream original, Stream result, Stream output)
    {
        using var raw = CreateTemp();
        var encoder = new VcEncoder(original, result, raw);
        var status = encoder.Encode(interleaved: false, checksumFormat: ChecksumFormat.SDCH);
        if (status != VCDiffResult.SUCCESS)
        {
            throw new PatchException($"Não foi possível calcular a diferença entre os arquivos ({status}).");
        }

        raw.Position = 0;
        using var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true);
        raw.CopyTo(brotli);
    }

    /// <summary>Aplica o delta comprimido sobre <paramref name="original"/> e escreve o resultado em <paramref name="output"/>.</summary>
    public static void Decode(Stream original, Stream compressedDelta, Stream output)
    {
        using var raw = CreateTemp();
        using (var brotli = new BrotliStream(compressedDelta, CompressionMode.Decompress, leaveOpen: true))
        {
            brotli.CopyTo(raw);
        }

        raw.Position = 0;
        var decoder = new VcDecoder(original, raw, output);
        var status = decoder.Decode(out _);
        if (status != VCDiffResult.SUCCESS)
        {
            throw new PatchException($"O patch está corrompido ou não é para este arquivo ({status}).");
        }
    }

    /// <summary>Conteúdo de um arquivo criado pelo patch: o jogo não tem o original, então não há o que diferenciar.</summary>
    public static void EncodeNew(Stream result, Stream output)
    {
        using var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true);
        result.CopyTo(brotli);
    }

    public static void DecodeNew(Stream compressed, Stream output)
    {
        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        brotli.CopyTo(output);
    }
}
