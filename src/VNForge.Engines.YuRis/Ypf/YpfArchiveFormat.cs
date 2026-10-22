using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>O formato YPF (<c>ysbin.ypf</c> e similares) do YU-RIS: lê o cabeçalho e o índice.</summary>
public sealed class YpfArchiveFormat : IArchiveFormat
{
    private const int MaxSampledEntries = 16;
    private const uint MaxSampledEntrySize = 4 * 1024 * 1024;
    private const long MaxSampledBytes = 8 * 1024 * 1024;

    public string Id => "ypf";

    public IReadOnlyList<string> FileExtensions { get; } = [".ypf"];

    /// <summary>Verdadeiro quando o fluxo começa com a assinatura <c>YPF\0</c> e tem os 32 bytes do cabeçalho. A versão só é conferida em <see cref="Open"/>.</summary>
    public bool CanOpen(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        try
        {
            var original = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                Span<byte> head = stackalloc byte[YpfHeader.Size];
                return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
                    && YpfHeader.HasMagic(head);
            }
            finally
            {
                stream.Seek(original, SeekOrigin.Begin);
            }
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lê o cabeçalho e o índice. O hash dos dados é detectado numa amostra das menores entradas; um hash de dados
    /// errado numa entrada só aparece ao abri-la (#28), desde que outra entrada da amostra confirme o algoritmo.
    /// </summary>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora do intervalo conhecido.</exception>
    /// <exception cref="InvalidFileException">
    /// Não é um YPF, o índice é inconsistente (hash do nome, <c>header_size</c>, offsets, nomes repetidos) ou o
    /// hash dos dados não confere com nenhum algoritmo conhecido.
    /// </exception>
    public IArchive Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("O fluxo do YPF precisa ser legível e permitir busca (seekable).", nameof(stream));
        }

        var source = stream is FileStream file ? file.Name : "<fluxo>";
        try
        {
            return OpenCore(stream, source);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidFileException(source, "o arquivo termina antes do que o cabeçalho e o índice indicam.", ex);
        }
    }

    private static YpfArchive OpenCore(Stream stream, string source)
    {
        stream.Seek(0, SeekOrigin.Begin);
        var length = stream.Length;

        var head = new byte[YpfHeader.Size];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (read < head.Length || !YpfHeader.HasMagic(head))
        {
            throw new InvalidFileException(
                source,
                read < head.Length
                    ? $"tem {read} bytes e o cabeçalho do YPF precisa de {YpfHeader.Size}."
                    : "não começa com a assinatura \"YPF\\0\"; não é um arquivo YPF.");
        }

        YpfHeader.TryParse(head, out var header);
        if (!YpfVersionDefaults.IsSupported(header.Version))
        {
            throw new UnknownVersionException(source, header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var available = length - YpfHeader.Size;
        if ((long)header.FileCount * YpfIndexReader.MinEntrySize > available)
        {
            throw new InvalidFileException(
                source,
                $"o cabeçalho declara {header.FileCount} arquivos, que precisam de pelo menos "
                + $"{(long)header.FileCount * YpfIndexReader.MinEntrySize} bytes de índice, mas o arquivo tem só {available} depois do cabeçalho.");
        }

        var indexBytes = (int)Math.Min(available, (long)header.FileCount * YpfIndexReader.MaxEntrySize);
        var index = new byte[indexBytes];
        stream.ReadExactly(index);

        var result = YpfIndexReader.Read(header, index, length, source);
        var dataHash = DetectDataHash(stream, result.Entries, header.Version, source);
        var parameters = result.Parameters with { DataHash = dataHash };
        return new YpfArchive(stream, header, parameters, result.Entries);
    }

    /// <summary>
    /// Compara o <c>data_hash</c> do índice com os três algoritmos, sobre os bytes armazenados das menores entradas.
    /// Vence o algoritmo que confere em mais entradas; no empate (por exemplo, entradas vazias) vale o padrão da versão.
    /// </summary>
    private static YpfHashAlgorithm DetectDataHash(Stream stream, IReadOnlyList<YpfEntry> entries, uint version, string source)
    {
        var preferred = YpfVersionDefaults.DataHash(version);
        var samples = new List<YpfEntry>();
        var seen = new HashSet<(ulong, uint)>();
        long total = 0;
        foreach (var entry in entries.Where(e => e.SizeStored <= MaxSampledEntrySize).OrderBy(e => e.SizeStored))
        {
            if (samples.Count >= MaxSampledEntries || total + entry.SizeStored > MaxSampledBytes)
            {
                break;
            }

            if (seen.Add((entry.Offset, entry.SizeStored)))
            {
                samples.Add(entry);
                total += entry.SizeStored;
            }
        }

        if (samples.Count == 0)
        {
            return preferred;
        }

        var algorithms = YpfIndexReader.Ordered(
            preferred,
            [YpfHashAlgorithm.Adler32, YpfHashAlgorithm.MurmurHash2, YpfHashAlgorithm.Crc32]);
        var matches = new int[algorithms.Count];
        var buffer = new byte[samples.Max(e => (int)e.SizeStored)];
        foreach (var entry in samples)
        {
            var data = buffer.AsSpan(0, (int)entry.SizeStored);
            stream.Seek((long)entry.Offset, SeekOrigin.Begin);
            stream.ReadExactly(data);
            for (var i = 0; i < algorithms.Count; i++)
            {
                if (YpfHash.Compute(algorithms[i], data) == entry.DataHash)
                {
                    matches[i]++;
                }
            }
        }

        var best = 0;
        for (var i = 1; i < matches.Length; i++)
        {
            if (matches[i] > matches[best])
            {
                best = i;
            }
        }

        if (matches[best] == 0)
        {
            throw new InvalidFileException(
                source,
                $"o hash dos dados de {samples.Count} entrada(s) lida(s) por amostragem (a primeira é '{samples[0].Path}') "
                + "não confere com Adler32, CRC32 nem MurmurHash2; o índice ou os dados estão corrompidos.");
        }

        return algorithms[best];
    }
}
