using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>
/// Monta em memória um YPF de qualquer variante a partir de arquivos de conteúdo conhecido, seguindo o
/// doc 06 (docs/06-yuris-notas-tecnicas.md). É independente do leitor: serve para os testes conferirem
/// o que o leitor e o escritor fazem, sem usar arquivos de jogo (CLAUDE.md, regra 1).
/// </summary>
/// <remarks>
/// Todo padrão vem de uma fonte citada no doc 06 e quase todo é configurável, porque as implementações de
/// referência divergem (versão do MurmurHash2, tabela de troca, chave do nome, header_size, ordem dos dados).
/// Os pontos a confirmar no jogo estão na lista da #23 do doc.
/// </remarks>
public sealed class SyntheticYpfBuilder
{
    private const int MaxNameBytes = 255;

    // Sem fallback: um caractere fora do Shift-JIS viraria '?' e dois nomes diferentes colidiriam.
    private static readonly Encoding StrictShiftJis = Encoding.GetEncoding(
        GameEncodings.ShiftJis.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    private readonly uint _version;
    private readonly List<PendingFile> _files = [];
    private byte? _nameKey;
    private SyntheticYpfSwapTable? _swapTable;
    private SyntheticYpfHashAlgorithm? _nameHash;
    private SyntheticYpfHashAlgorithm? _dataHash;
    private SyntheticYpfOffsetSize? _offsetSize;
    private bool? _headerSizeIncludesHeader;
    private SyntheticYpfDataOrder _dataOrder = SyntheticYpfDataOrder.Index;
    private bool _shareDuplicateData;

    public SyntheticYpfBuilder(uint version)
    {
        _version = version;
    }

    /// <summary>
    /// A chave K do nome, no sentido do YPF-Manager: cada byte do nome é gravado como <c>~(byte ^ K)</c>, então o XOR
    /// efetivo é <c>K ^ 0xFF</c>. Padrão pela versão: 0x00, 0x40 na v290 e 0x36 na v500 (<see cref="SyntheticYpfDefaults.NameKey"/>).
    /// </summary>
    public SyntheticYpfBuilder WithNameKey(byte key)
    {
        _nameKey = key;
        return this;
    }

    /// <summary>Tabela de troca do tamanho do nome. Padrão: a da v500 só na versão 500 exata, a padrão nas demais.</summary>
    public SyntheticYpfBuilder WithSwapTable(SyntheticYpfSwapTable table)
    {
        _swapTable = table;
        return this;
    }

    /// <summary>Hash do nome (calculado sobre os bytes Shift-JIS em claro). Padrão pela versão (Adler32 abaixo de 265, CRC32 até 478, MurmurHash2 a partir de 479).</summary>
    public SyntheticYpfBuilder WithNameHash(SyntheticYpfHashAlgorithm algorithm)
    {
        _nameHash = algorithm;
        return this;
    }

    /// <summary>Hash dos bytes armazenados. Padrão pela versão (Adler32 abaixo de 479, MurmurHash2 a partir dela). Independente do hash do nome e do tamanho do offset.</summary>
    public SyntheticYpfBuilder WithDataHash(SyntheticYpfHashAlgorithm algorithm)
    {
        _dataHash = algorithm;
        return this;
    }

    /// <summary>Largura do offset. Padrão: 32 bits abaixo de 479 e 64 bits a partir dela. Independente dos hashes.</summary>
    public SyntheticYpfBuilder WithOffsetSize(SyntheticYpfOffsetSize size)
    {
        _offsetSize = size;
        return this;
    }

    /// <summary>
    /// Se o <c>header_size</c> inclui os 32 bytes do cabeçalho fixo. <c>null</c> volta ao padrão pela versão:
    /// inclui a partir da v300 (yuri.md:459-462); abaixo dela só o tamanho do índice.
    /// </summary>
    public SyntheticYpfBuilder WithHeaderSizeIncludesHeader(bool? includesHeader)
    {
        _headerSizeIncludesHeader = includesHeader;
        return this;
    }

    /// <summary>Ordem dos bytes armazenados na área de dados. Padrão: a ordem do índice.</summary>
    public SyntheticYpfBuilder WithDataOrder(SyntheticYpfDataOrder order)
    {
        _dataOrder = order;
        return this;
    }

    /// <summary>
    /// Entradas com os mesmos bytes armazenados, mesmo tamanho original e mesmo flag de compressão passam a apontar para o
    /// mesmo offset, e a área de dados guarda uma cópia só (YPF-Manager, YPFArchive.cs:136-141).
    /// </summary>
    public SyntheticYpfBuilder WithSharedDuplicateData(bool shared = true)
    {
        _shareDuplicateData = shared;
        return this;
    }

    /// <summary>
    /// Adiciona um arquivo. O nome é gravado em Shift-JIS; com <paramref name="compress"/> os bytes armazenados são um fluxo
    /// zlib, mas só se ficarem menores que o original (yuri.py:131, YPFArchive.cs:130). <paramref name="forceCompression"/>
    /// grava o zlib mesmo quando ele não encolhe.
    /// </summary>
    public SyntheticYpfBuilder AddFile(
        string name,
        byte[] content,
        SyntheticYpfFileType fileType = SyntheticYpfFileType.Ybn,
        bool compress = false,
        bool forceCompression = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);

        byte[] nameBytes;
        try
        {
            nameBytes = StrictShiftJis.GetBytes(name);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException($"O nome '{name}' tem caractere que o Shift-JIS não representa.", nameof(name), ex);
        }

        if (nameBytes.Length > MaxNameBytes)
        {
            throw new ArgumentException($"O nome tem {nameBytes.Length} bytes; o máximo é {MaxNameBytes}.", nameof(name));
        }

        if (_files.Any(f => f.Name == name))
        {
            throw new ArgumentException($"Já existe um arquivo chamado '{name}'.", nameof(name));
        }

        _files.Add(new PendingFile(name, nameBytes, content, fileType, compress || forceCompression, forceCompression));
        return this;
    }

    public SyntheticYpf Build()
    {
        var nameKey = _nameKey ?? SyntheticYpfDefaults.NameKey(_version);
        var swapTable = _swapTable ?? SyntheticYpfDefaults.SwapTable(_version);
        var nameHash = _nameHash ?? SyntheticYpfDefaults.NameHash(_version);
        var dataHash = _dataHash ?? SyntheticYpfDefaults.DataHash(_version);
        var offsetSize = _offsetSize ?? SyntheticYpfDefaults.OffsetSize(_version);
        var includesHeader = _headerSizeIncludesHeader ?? SyntheticYpfDefaults.HeaderSizeIncludesHeader(_version);
        var offsetBytes = offsetSize == SyntheticYpfOffsetSize.Bits64 ? 8 : 4;

        var prepared = _files.Select(f => Prepare(f, nameHash, dataHash)).ToArray();

        // O índice é ordenado pelo hash do nome (GARbro ArcYPF.cs:271, YPF-Manager YPFArchive.cs:84);
        // empate fica na ordem de inserção.
        var indexOrder = prepared
            .Select((file, insertion) => (file, insertion))
            .OrderBy(x => x.file.NameHash)
            .ThenBy(x => x.insertion)
            .Select(x => x.file)
            .ToArray();

        var indexSize = indexOrder.Sum(f => EntrySize(f, offsetBytes));
        var dataStart = checked((uint)(SyntheticYpf.FixedHeaderSize + indexSize));
        var headerSize = includesHeader ? dataStart : (uint)indexSize;

        var layout = LayOutData(_dataOrder == SyntheticYpfDataOrder.Index ? indexOrder : prepared, dataStart);

        if (offsetSize == SyntheticYpfOffsetSize.Bits32 && layout.End > uint.MaxValue)
        {
            throw new InvalidOperationException("Os dados passam de 4 GiB e não cabem num offset de 32 bits.");
        }

        var bytes = new byte[layout.End];
        WriteHeader(bytes, headerSize, (uint)prepared.Length);

        var entries = new List<SyntheticYpfEntry>(prepared.Length);
        var position = SyntheticYpf.FixedHeaderSize;
        foreach (var file in indexOrder)
        {
            var entry = WriteEntry(bytes, position, file, layout.Offsets[file], nameKey, swapTable, offsetSize, offsetBytes);
            entries.Add(entry);
            position += entry.IndexEntrySize;
        }

        foreach (var (file, offset) in layout.Written)
        {
            file.Stored.CopyTo(bytes, (int)offset);
        }

        return new SyntheticYpf(bytes, _version, headerSize, includesHeader, nameKey, swapTable, nameHash, dataHash, offsetSize, entries)
        {
            DataStart = dataStart,
        };
    }

    private DataLayout LayOutData(IEnumerable<Prepared> order, uint dataStart)
    {
        var offsets = new Dictionary<Prepared, ulong>();
        var written = new List<(Prepared File, ulong Offset)>();
        var shared = new Dictionary<string, ulong>();
        var next = (ulong)dataStart;

        foreach (var file in order)
        {
            var key = $"{file.Source.Content.Length}:{file.IsCompressed}:{Convert.ToBase64String(file.Stored)}";
            if (_shareDuplicateData && shared.TryGetValue(key, out var existing))
            {
                offsets[file] = existing;
                continue;
            }

            offsets[file] = next;
            written.Add((file, next));
            shared[key] = next;
            next += (ulong)file.Stored.Length;
        }

        return new DataLayout(offsets, written, next);
    }

    private static Prepared Prepare(PendingFile file, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var stored = file.Content;
        var isCompressed = false;
        if (file.Compress)
        {
            var compressed = CompressZlib(file.Content);
            if (file.ForceCompression || compressed.Length < file.Content.Length)
            {
                stored = compressed;
                isCompressed = true;
            }
        }

        return new Prepared(file, stored, isCompressed, Hash(nameHash, file.NameBytes), Hash(dataHash, stored));
    }

    private static int EntrySize(Prepared file, int offsetBytes) =>
        4 + 1 + file.Source.NameBytes.Length + 1 + 1 + 4 + 4 + offsetBytes + 4;

    private void WriteHeader(byte[] bytes, uint headerSize, uint fileCount)
    {
        "YPF\0"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), _version);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), fileCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), headerSize);
        // Bytes 16 a 31: padding de zeros.
    }

    private static SyntheticYpfEntry WriteEntry(
        byte[] bytes,
        int position,
        Prepared file,
        ulong offset,
        byte nameKey,
        SyntheticYpfSwapTable swapTable,
        SyntheticYpfOffsetSize offsetSize,
        int offsetBytes)
    {
        var source = file.Source;
        var span = bytes.AsSpan(position);

        BinaryPrimitives.WriteUInt32LittleEndian(span, file.NameHash);

        // name_size: o tamanho passa pela tabela de troca e depois é invertido (~), o inverso da leitura.
        var encodedNameSize = (byte)~SyntheticYpfSwapTables.Swap(swapTable, (byte)source.NameBytes.Length);
        span[4] = encodedNameSize;

        // Cada byte do nome vira ~(byte ^ K) (YPF-Manager YPFArchive.cs:219; yuri.py:42-44).
        var obfuscated = source.NameBytes.Select(b => (byte)~(b ^ nameKey)).ToArray();
        obfuscated.CopyTo(span[5..]);

        var cursor = 5 + obfuscated.Length;
        span[cursor] = (byte)source.FileType;
        span[cursor + 1] = file.IsCompressed ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(cursor + 2)..], checked((uint)source.Content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span[(cursor + 6)..], checked((uint)file.Stored.Length));

        cursor += 10;
        if (offsetSize == SyntheticYpfOffsetSize.Bits64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[cursor..], offset);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], checked((uint)offset));
        }

        cursor += offsetBytes;
        BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], file.DataHash);

        return new SyntheticYpfEntry(
            source.Name,
            source.NameBytes,
            obfuscated,
            encodedNameSize,
            source.FileType,
            file.IsCompressed,
            (uint)source.Content.Length,
            (uint)file.Stored.Length,
            offset,
            file.NameHash,
            file.DataHash,
            position,
            cursor + 4,
            source.Content,
            file.Stored);
    }

    // Zlib de zero bytes: o ZLibStream não grava nada quando nenhum byte passou por ele, e um fluxo vazio não é zlib.
    private static readonly byte[] EmptyZlib = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];

    private static byte[] CompressZlib(byte[] content)
    {
        if (content.Length == 0)
        {
            return EmptyZlib;
        }

        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return output.ToArray();
    }

    private static uint Hash(SyntheticYpfHashAlgorithm algorithm, byte[] data) => algorithm switch
    {
        SyntheticYpfHashAlgorithm.Adler32 => YpfHash.Adler32(data),
        SyntheticYpfHashAlgorithm.Crc32 => YpfHash.Crc32(data),
        SyntheticYpfHashAlgorithm.MurmurHash2 => YpfHash.MurmurHash2(data),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private sealed record DataLayout(Dictionary<Prepared, ulong> Offsets, List<(Prepared File, ulong Offset)> Written, ulong End);

    private sealed record PendingFile(string Name, byte[] NameBytes, byte[] Content, SyntheticYpfFileType FileType, bool Compress, bool ForceCompression);

    private sealed class Prepared(PendingFile source, byte[] stored, bool isCompressed, uint nameHash, uint dataHash)
    {
        public PendingFile Source { get; } = source;

        public byte[] Stored { get; } = stored;

        public bool IsCompressed { get; } = isCompressed;

        public uint NameHash { get; } = nameHash;

        public uint DataHash { get; } = dataHash;
    }
}
