using System.Buffers.Binary;
using System.IO.Compression;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>
/// Monta em memória um YPF de qualquer variante a partir de arquivos de conteúdo conhecido, seguindo o
/// doc 06 (docs/06-yuris-notas-tecnicas.md). É independente do leitor: serve para os testes conferirem
/// o que o leitor e o escritor fazem, sem usar arquivos de jogo (CLAUDE.md, regra 1).
/// </summary>
/// <remarks>
/// Onde o doc 06 não define o campo, o gerador adota hipóteses a confirmar no jogo (#23): ver a seção
/// "Hipóteses do YPF sintético" do doc.
/// </remarks>
public sealed class SyntheticYpfBuilder
{
    private const int MaxNameBytes = 255;
    private const byte DefaultNameKey = 0x40;

    private readonly uint _version;
    private readonly List<PendingFile> _files = [];
    private byte _nameKey = DefaultNameKey;
    private SyntheticYpfSwapTable? _swapTable;
    private SyntheticYpfHashAlgorithm? _nameHash;
    private SyntheticYpfHashAlgorithm? _dataHash;
    private SyntheticYpfOffsetSize? _offsetSize;

    public SyntheticYpfBuilder(uint version)
    {
        _version = version;
    }

    /// <summary>Chave XOR aplicada a cada byte do nome no índice. Padrão 0x40.</summary>
    public SyntheticYpfBuilder WithNameKey(byte key)
    {
        _nameKey = key;
        return this;
    }

    /// <summary>Tabela de troca do tamanho do nome. Padrão: a da v500 só na versão 500, a padrão nas demais.</summary>
    public SyntheticYpfBuilder WithSwapTable(SyntheticYpfSwapTable table)
    {
        _swapTable = table;
        return this;
    }

    /// <summary>Hash do nome. Padrão pela versão (Adler32 abaixo de 265, CRC32 até 478, MurmurHash2 a partir de 479).</summary>
    public SyntheticYpfBuilder WithNameHash(SyntheticYpfHashAlgorithm algorithm)
    {
        _nameHash = algorithm;
        return this;
    }

    /// <summary>Hash dos bytes armazenados. Padrão pela versão (Adler32 abaixo de 479, MurmurHash2 a partir dela).</summary>
    public SyntheticYpfBuilder WithDataHash(SyntheticYpfHashAlgorithm algorithm)
    {
        _dataHash = algorithm;
        return this;
    }

    /// <summary>Largura do offset. Padrão: 32 bits abaixo de 479 e 64 bits a partir dela.</summary>
    public SyntheticYpfBuilder WithOffsetSize(SyntheticYpfOffsetSize size)
    {
        _offsetSize = size;
        return this;
    }

    /// <summary>Adiciona um arquivo. O nome é gravado em Shift-JIS; com <paramref name="compress"/> os bytes armazenados são um fluxo zlib.</summary>
    public SyntheticYpfBuilder AddFile(string name, byte[] content, SyntheticYpfFileType fileType = SyntheticYpfFileType.Ybn, bool compress = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);

        var nameBytes = GameEncodings.ShiftJis.GetBytes(name);
        if (nameBytes.Length > MaxNameBytes)
        {
            throw new ArgumentException($"O nome tem {nameBytes.Length} bytes; o máximo é {MaxNameBytes}.", nameof(name));
        }

        if (_files.Any(f => f.Name == name))
        {
            throw new ArgumentException($"Já existe um arquivo chamado '{name}'.", nameof(name));
        }

        _files.Add(new PendingFile(name, nameBytes, content, fileType, compress));
        return this;
    }

    public SyntheticYpf Build()
    {
        var swapTable = _swapTable ?? SyntheticYpfDefaults.SwapTable(_version);
        var nameHash = _nameHash ?? SyntheticYpfDefaults.NameHash(_version);
        var dataHash = _dataHash ?? SyntheticYpfDefaults.DataHash(_version);
        var offsetSize = _offsetSize ?? SyntheticYpfDefaults.OffsetSize(_version);
        var offsetBytes = offsetSize == SyntheticYpfOffsetSize.Bits64 ? 8 : 4;

        var prepared = _files.Select(f => Prepare(f, nameHash, dataHash)).ToArray();

        // Hipótese: o índice é ordenado pelo hash do nome (empate: ordem de inserção) e os dados
        // seguem o índice na ordem de inserção, logo os offsets não são crescentes no índice.
        var indexOrder = prepared
            .Select((file, insertion) => (file, insertion))
            .OrderBy(x => x.file.NameHash)
            .ThenBy(x => x.insertion)
            .Select(x => x.file)
            .ToArray();

        var indexSize = indexOrder.Sum(f => EntrySize(f, offsetBytes));
        var headerSize = checked((uint)(SyntheticYpf.FixedHeaderSize + indexSize));

        var offsets = new Dictionary<Prepared, ulong>();
        var nextOffset = (ulong)headerSize;
        foreach (var file in prepared)
        {
            offsets[file] = nextOffset;
            nextOffset += (ulong)file.Stored.Length;
        }

        if (offsetSize == SyntheticYpfOffsetSize.Bits32 && nextOffset > uint.MaxValue)
        {
            throw new InvalidOperationException("Os dados passam de 4 GiB e não cabem num offset de 32 bits.");
        }

        var bytes = new byte[nextOffset];
        WriteHeader(bytes, headerSize, (uint)prepared.Length);

        var entries = new List<SyntheticYpfEntry>(prepared.Length);
        var position = SyntheticYpf.FixedHeaderSize;
        foreach (var file in indexOrder)
        {
            var entry = WriteEntry(bytes, position, file, offsets[file], swapTable, offsetSize, offsetBytes);
            entries.Add(entry);
            position += entry.IndexEntrySize;

            file.Stored.CopyTo(bytes, (int)offsets[file]);
        }

        return new SyntheticYpf(bytes, _version, headerSize, _nameKey, swapTable, nameHash, dataHash, offsetSize, entries);
    }

    private static Prepared Prepare(PendingFile file, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var stored = file.Compress ? CompressZlib(file.Content) : file.Content;
        return new Prepared(file, stored, Hash(nameHash, file.NameBytes), Hash(dataHash, stored));
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

    private SyntheticYpfEntry WriteEntry(
        byte[] bytes,
        int position,
        Prepared file,
        ulong offset,
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

        var obfuscated = source.NameBytes.Select(b => (byte)(b ^ _nameKey)).ToArray();
        obfuscated.CopyTo(span[5..]);

        var cursor = 5 + obfuscated.Length;
        span[cursor] = (byte)source.FileType;
        span[cursor + 1] = source.Compress ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(cursor + 2)..], checked((uint)source.Content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span[(cursor + 6)..], checked((uint)file.Stored.Length));

        cursor += 10;
        if (offsetSize == SyntheticYpfOffsetSize.Bits64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[cursor..], offset);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], (uint)offset);
        }

        cursor += offsetBytes;
        BinaryPrimitives.WriteUInt32LittleEndian(span[cursor..], file.DataHash);

        return new SyntheticYpfEntry(
            source.Name,
            source.NameBytes,
            obfuscated,
            encodedNameSize,
            source.FileType,
            source.Compress,
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

    private static byte[] CompressZlib(byte[] content)
    {
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

    private sealed record PendingFile(string Name, byte[] NameBytes, byte[] Content, SyntheticYpfFileType FileType, bool Compress);

    private sealed class Prepared(PendingFile source, byte[] stored, uint nameHash, uint dataHash)
    {
        public PendingFile Source { get; } = source;

        public byte[] Stored { get; } = stored;

        public uint NameHash { get; } = nameHash;

        public uint DataHash { get; } = dataHash;
    }
}
