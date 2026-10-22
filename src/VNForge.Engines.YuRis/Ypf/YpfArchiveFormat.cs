using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>O formato YPF (<c>ysbin.ypf</c> e similares) do YU-RIS: lê o cabeçalho e o índice.</summary>
public sealed class YpfArchiveFormat : IArchiveFormat
{
    private const long MaxIndexBytes = 128 * 1024 * 1024;
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
    /// Lê o cabeçalho e o índice de qualquer versão: o que vale é o que confere com o conteúdo. O hash dos dados é
    /// detectado numa amostra das menores entradas; se a amostra não o confirma, vale o padrão da versão e
    /// <see cref="YpfParameters.DataHashVerified"/> fica falso. A conferência por entrada é de <c>OpenEntry</c> (#28).
    /// </summary>
    /// <exception cref="UnknownVersionException">Nenhuma combinação leu o índice e a versão está fora das versões conhecidas.</exception>
    /// <exception cref="InvalidFileException">
    /// Não é um YPF ou o índice é inconsistente (hash do nome, <c>header_size</c>, offsets, nomes repetidos).
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
        try
        {
            return ReadArchive(stream, header, length, source);
        }
        catch (InvalidFileException ex) when (!YpfVersionDefaults.IsKnownVersion(header.Version))
        {
            throw new UnknownVersionException(
                source,
                $"{header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}, fora das versões conhecidas "
                + $"({YpfVersionDefaults.MinKnownVersion} a {YpfVersionDefaults.MaxKnownVersion}), e nenhuma combinação de offset, tabela e hash leu o índice",
                ex);
        }
    }

    private static YpfArchive ReadArchive(Stream stream, YpfHeader header, long length, string source)
    {
        var available = length - YpfHeader.Size;
        var minIndexBytes = (long)header.FileCount * YpfIndexReader.MinEntrySize;
        if (minIndexBytes > available)
        {
            throw new InvalidFileException(
                source,
                $"o cabeçalho declara {header.FileCount} arquivos, que precisam de pelo menos "
                + $"{minIndexBytes} bytes de índice, mas o arquivo tem só {available} depois do cabeçalho.");
        }

        if (minIndexBytes > MaxIndexBytes)
        {
            throw new InvalidFileException(
                source,
                $"o cabeçalho declara {header.FileCount} arquivos, e o índice passaria do limite de {MaxIndexBytes / (1024 * 1024)} MiB que o leitor aceita.");
        }

        var wanted = Math.Min(available, (long)header.FileCount * YpfIndexReader.MaxEntrySize);
        var capped = wanted > MaxIndexBytes;
        var fallbackBytes = (int)Math.Min(wanted, MaxIndexBytes);

        // O header_size diz onde o índice acaba, nas duas semânticas (só o índice, ou o índice mais 32 bytes), então
        // ele serve de tamanho de leitura. Se estiver errado, o índice é lido de novo com o teto pela contagem.
        var hintedBytes = (int)Math.Min(header.HeaderSize, fallbackBytes);
        YpfIndexReader.IndexResult? result = null;
        if (hintedBytes < fallbackBytes)
        {
            try
            {
                result = ReadIndex(stream, header, hintedBytes, length, source);
            }
            catch (InvalidFileException)
            {
            }
        }

        try
        {
            result ??= ReadIndex(stream, header, fallbackBytes, length, source);
        }
        catch (InvalidFileException ex) when (capped)
        {
            throw new InvalidFileException(
                source,
                $"{ex.Reason} O leitor carrega no máximo {MaxIndexBytes / (1024 * 1024)} MiB do índice e o arquivo pede mais.",
                ex);
        }

        var (dataHash, verified) = DetectDataHash(stream, result.Entries, header.Version);
        var parameters = result.Parameters with { DataHash = dataHash, DataHashVerified = verified };
        return new YpfArchive(stream, header, parameters, result.Entries);
    }

    private static YpfIndexReader.IndexResult ReadIndex(Stream stream, YpfHeader header, int bytes, long length, string source)
    {
        var index = new byte[bytes];
        stream.Seek(YpfHeader.Size, SeekOrigin.Begin);
        stream.ReadExactly(index);
        return YpfIndexReader.Read(header, index, length, source);
    }

    /// <summary>
    /// Compara o <c>data_hash</c> do índice com os três algoritmos, sobre os bytes armazenados das menores entradas.
    /// Vence o algoritmo que confere em mais entradas; no empate (por exemplo, entradas vazias) vale o primeiro na ordem
    /// de preferência, com o padrão da versão na frente. Sem nenhuma entrada conferindo, vale o padrão da versão e o
    /// resultado vem como não verificado; não é erro.
    /// </summary>
    private static (YpfHashAlgorithm Algorithm, bool Verified) DetectDataHash(Stream stream, IReadOnlyList<YpfEntry> entries, uint version)
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
            return (preferred, false);
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

        // Sem nenhuma confirmação (ou com empate, como em entradas vazias), vale o padrão da versão: o detalhe
        // por entrada fica com OpenEntry (#28), e um data_hash divergente não impede de abrir o resto do pacote.
        var verified = matches[best] > 0 && matches.Count(m => m == matches[best]) == 1;
        return (matches[best] > 0 ? algorithms[best] : preferred, verified);
    }
}
