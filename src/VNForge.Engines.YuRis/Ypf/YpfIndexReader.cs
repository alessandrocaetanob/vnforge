using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Lê o índice de um YPF sem saber de antemão a largura do offset, a tabela de troca nem o algoritmo de hash do nome:
/// tenta as combinações conhecidas, a da versão primeiro, e aceita a que lê o índice inteiro com todos os hashes de
/// nome conferindo, o <c>header_size</c> consistente e os dados dentro do arquivo.
/// </summary>
internal static class YpfIndexReader
{
    public const int MinEntrySize = 4 + 1 + 1 + 1 + 1 + 4 + 4 + 4 + 4;
    public const int MaxEntrySize = 4 + 1 + byte.MaxValue + 1 + 1 + 4 + 4 + 8 + 4;

    private const int MaxNameLength = byte.MaxValue;
    private static readonly byte[] KnownNameXors = [0xFF, 0xBF, 0xC9];

    public sealed record IndexResult(IReadOnlyList<YpfEntry> Entries, YpfParameters Parameters);

    private readonly record struct Candidate(YpfOffsetSize OffsetSize, YpfSwapTable SwapTable, YpfHashAlgorithm NameHash);

    private sealed class Attempt(Candidate candidate)
    {
        public Candidate Candidate { get; } = candidate;

        public int ParsedEntries { get; set; }

        public string? Failure { get; set; }

        public IndexResult? Result { get; set; }
    }

    /// <param name="header">Cabeçalho já lido.</param>
    /// <param name="index">Bytes a partir do offset 0x20. Podem passar do fim do índice (os dados vêm logo depois).</param>
    /// <param name="fileLength">Tamanho do arquivo, para conferir os offsets.</param>
    /// <param name="source">Nome do arquivo, para as mensagens de erro.</param>
    /// <exception cref="InvalidFileException">Nenhuma combinação leu o índice; a mensagem descreve a tentativa que foi mais longe.</exception>
    public static IndexResult Read(YpfHeader header, byte[] index, long fileLength, string source)
    {
        Attempt? best = null;
        foreach (var candidate in Candidates(header.Version))
        {
            var attempt = TryRead(candidate, header, index, fileLength);
            if (attempt.Result is not null)
            {
                return attempt.Result;
            }

            if (best is null || attempt.ParsedEntries > best.ParsedEntries)
            {
                best = attempt;
            }
        }

        var describe = best!.Candidate;
        throw new InvalidFileException(
            source,
            $"não foi possível ler o índice do YPF versão {header.Version} ({header.FileCount} arquivos). "
            + $"Melhor tentativa: offset de {Bits(describe.OffsetSize)} bits, tabela {describe.SwapTable}, "
            + $"hash do nome {describe.NameHash}; {best.Failure}");
    }

    private static IEnumerable<Candidate> Candidates(uint version)
    {
        var offsets = Ordered(YpfVersionDefaults.OffsetSize(version), [YpfOffsetSize.Bits32, YpfOffsetSize.Bits64]);
        var tables = Ordered(YpfVersionDefaults.SwapTable(version), YpfSwapTables.All);
        var hashes = Ordered(
            YpfVersionDefaults.NameHash(version),
            [YpfHashAlgorithm.Adler32, YpfHashAlgorithm.Crc32, YpfHashAlgorithm.MurmurHash2]);

        foreach (var offset in offsets)
        {
            foreach (var table in tables)
            {
                foreach (var hash in hashes)
                {
                    yield return new Candidate(offset, table, hash);
                }
            }
        }
    }

    internal static List<T> Ordered<T>(T first, IEnumerable<T> all) where T : struct
    {
        var list = new List<T> { first };
        list.AddRange(all.Where(x => !EqualityComparer<T>.Default.Equals(x, first)));
        return list;
    }

    private static Attempt TryRead(Candidate candidate, YpfHeader header, byte[] index, long fileLength)
    {
        var attempt = new Attempt(candidate);
        var count = checked((int)header.FileCount);
        var entries = new List<YpfEntry>(Math.Min(count, index.Length / MinEntrySize));
        var nameKeys = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        byte? lastXor = null;
        var position = 0;

        for (var i = 0; i < count; i++)
        {
            var number = i + 1;
            var entry = ReadEntry(candidate, index, position, number, count, fileLength, lastXor, out var failureText);
            if (entry is null)
            {
                attempt.Failure = failureText;
                return attempt;
            }

            if (!nameKeys.Add(NormalizedNameKey(entry.NameBytes.Span)) || !paths.Add(entry.Path))
            {
                attempt.Failure = $"o nome '{entry.Path}' aparece em mais de uma entrada; o caminho de cada entrada deve ser único.";
                attempt.ParsedEntries = number;
                return attempt;
            }

            lastXor = entry.NameXor;
            entries.Add(entry);
            position += entry.IndexEntrySize;
            attempt.ParsedEntries = number;
        }

        var failure = CheckHeaderSize(header, position, out var includesHeader)
            ?? CheckOffsets(entries, YpfHeader.Size + position);
        if (failure is not null)
        {
            attempt.Failure = failure;
            return attempt;
        }

        var (gaps, trailer) = FindUnreferencedRanges(entries, YpfHeader.Size + position, fileLength);
        var parameters = new YpfParameters(
            header.Version,
            candidate.NameHash,
            YpfVersionDefaults.DataHash(header.Version),
            candidate.OffsetSize,
            includesHeader,
            candidate.SwapTable,
            MostCommonXor(entries, header.Version),
            position,
            fileLength,
            gaps,
            trailer,
            DataHashVerified: false);
        attempt.Result = new IndexResult(entries, parameters);
        return attempt;
    }

    private static YpfEntry? ReadEntry(
        Candidate candidate,
        byte[] index,
        int position,
        int number,
        int count,
        long fileLength,
        byte? lastXor,
        out string? failure)
    {
        failure = null;
        var offsetBytes = candidate.OffsetSize == YpfOffsetSize.Bits64 ? 8 : 4;
        if (index.Length - position < 5)
        {
            failure = $"o índice acaba antes da entrada {number} de {count}.";
            return null;
        }

        var span = index.AsSpan(position);
        var nameHash = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var encodedNameSize = span[4];
        var nameLength = YpfSwapTables.DecodeNameSize(candidate.SwapTable, encodedNameSize);
        if (nameLength == 0)
        {
            failure = $"o tamanho do nome da entrada {number} de {count} decodifica para 0 (byte gravado 0x{encodedNameSize:X2}).";
            return null;
        }

        var entrySize = 5 + nameLength + 1 + 1 + 4 + 4 + offsetBytes + 4;
        if (index.Length - position < entrySize)
        {
            failure = $"o índice acaba antes do fim da entrada {number} de {count}, que precisa de {entrySize} bytes.";
            return null;
        }

        var obfuscated = span.Slice(5, nameLength);
        if (!TryDecryptName(obfuscated, nameHash, candidate.NameHash, lastXor, out var xor, out var plain))
        {
            failure =
                $"o hash do nome da entrada {number} de {count} (0x{nameHash:X8}, posição {YpfHeader.Size + position}) "
                + "não confere com o nome decifrado por nenhuma chave conhecida nem deduzida.";
            return null;
        }

        var cursor = 5 + nameLength;
        var fileType = span[cursor];
        var compressed = span[cursor + 1];
        var sizeUncompressed = BinaryPrimitives.ReadUInt32LittleEndian(span[(cursor + 2)..]);
        var sizeStored = BinaryPrimitives.ReadUInt32LittleEndian(span[(cursor + 6)..]);
        cursor += 10;
        var offset = candidate.OffsetSize == YpfOffsetSize.Bits64
            ? BinaryPrimitives.ReadUInt64LittleEndian(span[cursor..])
            : BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
        cursor += offsetBytes;
        var dataHash = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);

        var path = GameEncodings.ShiftJis.GetString(plain).Replace('\\', '/');
        if (compressed > 1)
        {
            failure = $"a entrada {number} de {count} ('{path}') tem o campo compressed = {compressed}; o esperado é 0 ou 1.";
            return null;
        }

        if (offset > (ulong)fileLength || sizeStored > (ulong)fileLength - offset)
        {
            failure =
                $"a entrada {number} de {count} ('{path}') guarda {sizeStored} bytes a partir do offset {offset}, "
                + $"mas o arquivo tem só {fileLength} bytes (arquivo truncado ou offset de tamanho errado).";
            return null;
        }

        return new YpfEntry(
            path,
            sizeUncompressed,
            compressed == 1,
            nameHash,
            plain,
            obfuscated.ToArray(),
            encodedNameSize,
            xor,
            candidate.SwapTable,
            fileType,
            sizeStored,
            offset,
            dataHash,
            candidate.OffsetSize,
            YpfHeader.Size + position,
            entrySize);
    }

    private static string? CheckHeaderSize(YpfHeader header, int indexSize, out bool includesHeader)
    {
        var withHeader = (uint)(YpfHeader.Size + indexSize);
        var withoutHeader = (uint)indexSize;
        includesHeader = header.HeaderSize == withHeader;
        if (includesHeader || header.HeaderSize == withoutHeader)
        {
            return null;
        }

        return $"o header_size gravado é {header.HeaderSize}, mas o índice lido ocupa {indexSize} bytes; "
            + $"o esperado era {withoutHeader} (só o índice) ou {withHeader} (índice e os {YpfHeader.Size} bytes do cabeçalho).";
    }

    private static string? CheckOffsets(List<YpfEntry> entries, int dataStart)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].SizeStored > 0 && entries[i].Offset < (ulong)dataStart)
            {
                return $"a entrada {i + 1} de {entries.Count} ('{entries[i].Path}') aponta para o offset {entries[i].Offset}, "
                    + $"dentro do cabeçalho ou do índice; os dados começam no offset {dataStart}.";
            }
        }

        return null;
    }

    /// <summary>
    /// Chave de comparação de nomes: os bytes do nome com <c>\</c> e <c>/</c> equivalentes. O byte 0x5C também
    /// aparece como segundo byte de caracteres Shift-JIS de dois bytes, então os pares são pulados.
    /// </summary>
    private static string NormalizedNameKey(ReadOnlySpan<byte> plain)
    {
        var normalized = new char[plain.Length];
        var i = 0;
        while (i < plain.Length)
        {
            var b = plain[i];
            if (b is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC && i + 1 < plain.Length)
            {
                normalized[i] = (char)b;
                normalized[i + 1] = (char)plain[i + 1];
                i += 2;
                continue;
            }

            normalized[i] = b == (byte)'\\' ? '/' : (char)b;
            i++;
        }

        return new string(normalized);
    }

    /// <summary>Trechos da área de dados que nenhuma entrada com bytes armazenados cobre: lacunas e resto até o fim do arquivo.</summary>
    private static (List<YpfByteRange> Gaps, YpfByteRange Trailer) FindUnreferencedRanges(
        List<YpfEntry> entries,
        long dataStart,
        long fileLength)
    {
        var gaps = new List<YpfByteRange>();
        var cursor = dataStart;
        foreach (var entry in entries.Where(e => e.SizeStored > 0).OrderBy(e => e.Offset))
        {
            var start = (long)entry.Offset;
            if (start > cursor)
            {
                gaps.Add(new YpfByteRange(cursor, start - cursor));
            }

            cursor = Math.Max(cursor, start + entry.SizeStored);
        }

        return (gaps, new YpfByteRange(cursor, Math.Max(0, fileLength - cursor)));
    }

    private static byte MostCommonXor(List<YpfEntry> entries, uint version) =>
        entries.Count == 0
            ? YpfVersionDefaults.NameXor(version)
            : entries.GroupBy(e => e.NameXor).OrderByDescending(g => g.Count()).First().Key;

    private static int FindXor(
        ReadOnlySpan<byte> obfuscated,
        Span<byte> buffer,
        Span<bool> tried,
        YpfHashAlgorithm algorithm,
        uint expectedHash,
        byte? previousXor)
    {
        if (obfuscated.Length >= 4 && TryXor((byte)(obfuscated[^4] ^ '.'), obfuscated, buffer, tried, algorithm, expectedHash))
        {
            return obfuscated[^4] ^ '.';
        }

        if (previousXor is { } previous && TryXor(previous, obfuscated, buffer, tried, algorithm, expectedHash))
        {
            return previous;
        }

        foreach (var known in KnownNameXors)
        {
            if (TryXor(known, obfuscated, buffer, tried, algorithm, expectedHash))
            {
                return known;
            }
        }

        for (var candidate = 0; candidate < 256; candidate++)
        {
            if (TryXor((byte)candidate, obfuscated, buffer, tried, algorithm, expectedHash))
            {
                return candidate;
            }
        }

        return -1;
    }

    /// <summary>
    /// Procura o XOR efetivo que faz o hash do nome conferir. Tenta, nesta ordem: a dedução do GARbro (o byte
    /// quatro posições antes do fim é o <c>.</c> da extensão), o XOR da entrada anterior, os XOR conhecidos
    /// (0xFF, 0xBF, 0xC9) e, por último, os 256 valores.
    /// </summary>
    private static bool TryDecryptName(
        ReadOnlySpan<byte> obfuscated,
        uint expectedHash,
        YpfHashAlgorithm algorithm,
        byte? previousXor,
        out byte xor,
        out byte[] plain)
    {
        Span<byte> scratch = stackalloc byte[MaxNameLength];
        var buffer = scratch[..obfuscated.Length];
        Span<bool> tried = stackalloc bool[256];

        var found = FindXor(obfuscated, buffer, tried, algorithm, expectedHash, previousXor);
        if (found < 0)
        {
            xor = 0;
            plain = [];
            return false;
        }

        xor = (byte)found;
        plain = new byte[obfuscated.Length];
        for (var i = 0; i < plain.Length; i++)
        {
            plain[i] = (byte)(obfuscated[i] ^ xor);
        }

        return true;
    }

    private static bool TryXor(
        byte candidate,
        ReadOnlySpan<byte> obfuscated,
        Span<byte> buffer,
        Span<bool> tried,
        YpfHashAlgorithm algorithm,
        uint expectedHash)
    {
        if (tried[candidate])
        {
            return false;
        }

        tried[candidate] = true;
        for (var i = 0; i < obfuscated.Length; i++)
        {
            buffer[i] = (byte)(obfuscated[i] ^ candidate);
        }

        return YpfHash.Compute(algorithm, buffer) == expectedHash;
    }

    internal static int Bits(YpfOffsetSize size) => size == YpfOffsetSize.Bits64 ? 64 : 32;
}
