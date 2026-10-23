using System.Buffers.Binary;
using System.IO.Compression;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Grava um YPF com reescrita mínima (regra 3 do <c>CLAUDE.md</c>): o cabeçalho, o índice e a área de dados saem
/// do original, e só as entradas trocadas são recomprimidas e têm o hash dos dados recalculado.
/// </summary>
/// <remarks>
/// A área de dados é uma sequência de segmentos que cobre do fim do índice ao fim do arquivo: lacunas, blocos de
/// bytes armazenados (um por par offset e tamanho, então entradas com dados compartilhados dividem o bloco) e o
/// trailer. Segmentos sem troca são copiados crus do fluxo de origem, na mesma ordem. O bloco de uma entrada
/// trocada é substituído no lugar; se outra entrada ainda usa o bloco antigo, ele fica e o bloco novo vem logo
/// depois. Entrada vazia que passa a ter conteúdo ganha o bloco novo no fim dos dados, antes do trailer.
/// </remarks>
internal sealed class YpfWriter(YpfArchive archive, Stream source)
{
    private const int CopyBufferSize = 81920;

    private readonly YpfParameters _parameters = archive.Parameters;
    private readonly IReadOnlyList<YpfEntry> _entries = archive.Entries;

    private sealed record Change(byte[] Stored, uint Size, uint DataHash);

    /// <summary>Um trecho da área de dados: <c>Entries</c> vazio é lacuna ou trailer; senão, os índices das entradas que dividem o bloco.</summary>
    private sealed record Segment(long OldStart, long OldLength, List<int> Entries);

    /// <summary>Trecho do arquivo novo: bytes crus de <c>SourceStart</c> no original, ou <c>Bytes</c> novos.</summary>
    private sealed record Piece(long SourceStart, long Length, byte[]? Bytes);

    private sealed record Boundary(long OldStart, long OldLength, long NewStart, bool Kept);

    public void Write(Stream output, IReadOnlyDictionary<string, byte[]> replacements)
    {
        if (source.Length != _parameters.FileLength)
        {
            throw new InvalidDataException(
                $"O arquivo de origem tem {source.Length} bytes, mas o YPF foi aberto com {_parameters.FileLength}: ele mudou depois de aberto. Abra o pacote de novo.");
        }

        var changes = ResolveChanges(replacements);
        var changedAny = changes.Any(c => c is not null);

        var segments = TileDataArea();
        if (segments is null)
        {
            if (changedAny)
            {
                throw new NotSupportedException(
                    "Não é possível trocar entradas deste YPF: os dados de algumas entradas se sobrepõem parcialmente, "
                    + "e a reescrita mínima não sabe mover um bloco que outro compartilha pela metade.");
            }

            CopyRange(0, _parameters.FileLength, output);
            return;
        }

        var (pieces, offsets) = LayOut(segments, changes);
        var index = PatchIndex(changes, offsets);

        output.Write(BuildHeader());
        output.Write(index);
        foreach (var piece in pieces)
        {
            if (piece.Bytes is not null)
            {
                output.Write(piece.Bytes);
            }
            else
            {
                CopyRange(piece.SourceStart, piece.Length, output);
            }
        }
    }

    private Change?[] ResolveChanges(IReadOnlyDictionary<string, byte[]> replacements)
    {
        var byPath = new Dictionary<string, int>(_entries.Count, StringComparer.Ordinal);
        for (var i = 0; i < _entries.Count; i++)
        {
            byPath[_entries[i].Path] = i;
        }

        foreach (var (path, content) in replacements)
        {
            if (!byPath.ContainsKey(path))
            {
                throw new ArgumentException($"A entrada \"{path}\" não existe neste pacote.", nameof(replacements));
            }

            if (content is null)
            {
                throw new ArgumentException($"O conteúdo novo da entrada \"{path}\" é nulo.", nameof(replacements));
            }
        }

        var changes = new Change?[_entries.Count];
        foreach (var (path, content) in replacements)
        {
            var i = byPath[path];
            var entry = _entries[i];
            if (IsSameContent(entry, content))
            {
                continue;
            }

            if (!_parameters.DataHashVerified)
            {
                throw new NotSupportedException(
                    $"Não é possível gravar a entrada \"{entry.Path}\": o algoritmo do hash dos dados deste YPF não foi confirmado "
                    + "(nenhuma entrada conferiu com Adler-32, CRC-32 ou MurmurHash2), e gravar o hash padrão da versão "
                    + $"{_parameters.Version} ({_parameters.DataHash}) seria um palpite que o jogo pode recusar.");
            }

            var stored = entry.IsCompressed ? Deflate(content) : content;
            changes[i] = new Change(stored, (uint)content.Length, YpfHash.Compute(_parameters.DataHash, stored));
        }

        return changes;
    }

    /// <summary>
    /// Verdadeiro quando o conteúdo novo é igual ao atual. Uma entrada que não decodifica (dados corrompidos) conta
    /// como diferente: trocar a entrada corrompida é uma troca de verdade.
    /// </summary>
    private bool IsSameContent(YpfEntry entry, byte[] content)
    {
        if (content.Length != entry.Size)
        {
            return false;
        }

        try
        {
            using var current = archive.OpenEntry(entry);
            var actual = new byte[content.Length];
            current.ReadExactly(actual);
            return actual.AsSpan().SequenceEqual(content);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // Zlib de zero bytes: cabeçalho 78 9C, bloco final vazio e Adler-32 de 1. O ZLibStream não grava nada quando
    // nenhum byte passou por ele, e um fluxo vazio não é um zlib válido.
    private static readonly byte[] EmptyZlib = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];

    private static byte[] Deflate(byte[] content)
    {
        if (content.Length == 0)
        {
            return EmptyZlib;
        }

        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Divide a área de dados em segmentos contíguos. Devolve <c>null</c> quando os blocos das entradas se
    /// sobrepõem sem ser idênticos: aí os segmentos não ladrilham a área.
    /// </summary>
    private List<Segment>? TileDataArea()
    {
        var segments = _parameters.Gaps.Select(g => new Segment(g.Offset, g.Length, [])).ToList();
        var groups = Enumerable.Range(0, _entries.Count)
            .Where(i => _entries[i].SizeStored > 0)
            .GroupBy(i => (_entries[i].Offset, _entries[i].SizeStored));
        segments.AddRange(groups.Select(g => new Segment((long)g.Key.Offset, g.Key.SizeStored, g.ToList())));
        segments.Sort((a, b) => a.OldStart.CompareTo(b.OldStart));
        segments.Add(new Segment(_parameters.Trailer.Offset, _parameters.Trailer.Length, []));

        var cursor = (long)_parameters.DataStart;
        foreach (var segment in segments)
        {
            if (segment.OldStart != cursor)
            {
                return null;
            }

            cursor += segment.OldLength;
        }

        return cursor == _parameters.FileLength ? segments : null;
    }

    private (List<Piece> Pieces, ulong[] Offsets) LayOut(List<Segment> segments, Change?[] changes)
    {
        var layout = new Layout((long)_parameters.DataStart, _entries.Select(e => e.Offset).ToArray(), changes);
        for (var s = 0; s < segments.Count - 1; s++)
        {
            LaySegment(layout, segments[s], changes);
        }

        foreach (var i in EmptyEntries().Where(i => changes[i] is not null))
        {
            layout.AddNew(i);
        }

        var trailer = segments[^1];
        layout.Boundaries.Add(new Boundary(trailer.OldStart, trailer.OldLength, layout.Position, Kept: true));
        if (trailer.OldLength > 0)
        {
            layout.Pieces.Add(new Piece(trailer.OldStart, trailer.OldLength, null));
        }

        foreach (var i in EmptyEntries().Where(i => changes[i] is null))
        {
            layout.Offsets[i] = MapOffset(_entries[i].Offset, layout.Boundaries);
        }

        return (layout.Pieces, layout.Offsets);
    }

    private static void LaySegment(Layout layout, Segment segment, Change?[] changes)
    {
        var segmentStart = layout.Position;
        var kept = segment.Entries.Count == 0 || segment.Entries.Any(i => changes[i] is null);
        if (kept)
        {
            foreach (var i in segment.Entries.Where(i => changes[i] is null))
            {
                layout.Offsets[i] = (ulong)layout.Position;
            }

            layout.AddOriginal(segment.OldStart, segment.OldLength);
        }

        foreach (var i in segment.Entries.Where(i => changes[i] is not null))
        {
            layout.AddNew(i);
        }

        layout.Boundaries.Add(new Boundary(segment.OldStart, segment.OldLength, segmentStart, kept));
    }

    private IEnumerable<int> EmptyEntries() =>
        Enumerable.Range(0, _entries.Count).Where(i => _entries[i].SizeStored == 0);

    private sealed class Layout(long start, ulong[] offsets, Change?[] changes)
    {
        public List<Piece> Pieces { get; } = [];

        public List<Boundary> Boundaries { get; } = [];

        public ulong[] Offsets { get; } = offsets;

        public long Position { get; private set; } = start;

        public void AddOriginal(long oldStart, long oldLength)
        {
            Pieces.Add(new Piece(oldStart, oldLength, null));
            Position += oldLength;
        }

        public void AddNew(int entryIndex)
        {
            var stored = changes[entryIndex]!.Stored;
            Offsets[entryIndex] = (ulong)Position;
            Pieces.Add(new Piece(-1, stored.Length, stored));
            Position += stored.Length;
        }
    }

    /// <summary>
    /// Para uma entrada sem bytes: o offset acompanha o segmento onde cai (normalmente o limite entre dois blocos,
    /// ou o fim dos dados). Offset antes dos dados, dentro do cabeçalho ou do índice, não muda.
    /// </summary>
    private ulong MapOffset(ulong offset, List<Boundary> boundaries)
    {
        if (offset < (ulong)_parameters.DataStart)
        {
            return offset;
        }

        var target = boundaries.Last(b => (ulong)b.OldStart <= offset);
        var isTrailer = ReferenceEquals(target, boundaries[^1]);
        var delta = (long)offset - target.OldStart;
        return (ulong)(target.Kept || isTrailer ? target.NewStart + delta : target.NewStart);
    }

    private byte[] PatchIndex(Change?[] changes, ulong[] offsets)
    {
        var index = new byte[_parameters.IndexSize];
        try
        {
            source.Seek(YpfHeader.Size, SeekOrigin.Begin);
            source.ReadExactly(index);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("O índice do YPF de origem acaba antes do esperado: o arquivo mudou depois de aberto.", ex);
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var offsetBytes = entry.OffsetSize == YpfOffsetSize.Bits64 ? 8 : 4;
            var end = entry.IndexPosition - YpfHeader.Size + entry.IndexEntrySize;
            var offsetAt = end - 4 - offsetBytes;

            if (offsets[i] != entry.Offset)
            {
                WriteOffset(index.AsSpan(offsetAt), entry, offsetBytes, offsets[i]);
            }

            if (changes[i] is { } change)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(offsetAt - 8), change.Size);
                BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(offsetAt - 4), (uint)change.Stored.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(end - 4), change.DataHash);
            }
        }

        return index;
    }

    private static void WriteOffset(Span<byte> target, YpfEntry entry, int offsetBytes, ulong offset)
    {
        if (offsetBytes == 8)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(target, offset);
            return;
        }

        if (offset > uint.MaxValue)
        {
            throw new NotSupportedException(
                $"O arquivo novo passaria de 4 GiB e o offset da entrada \"{entry.Path}\" não cabe nos 32 bits que este YPF usa.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)offset);
    }

    private byte[] BuildHeader()
    {
        var header = archive.Header;
        var bytes = new byte[YpfHeader.Size];
        "YPF\0"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), header.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), header.FileCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), header.HeaderSize);
        header.Padding.Span.CopyTo(bytes.AsSpan(16));
        return bytes;
    }

    private void CopyRange(long start, long length, Stream output)
    {
        var buffer = new byte[(int)Math.Min(length, CopyBufferSize)];
        source.Seek(start, SeekOrigin.Begin);
        var remaining = length;
        try
        {
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(remaining, buffer.Length);
                source.ReadExactly(buffer.AsSpan(0, chunk));
                output.Write(buffer, 0, chunk);
                remaining -= chunk;
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"O YPF de origem acaba antes do esperado (offset {start}, {length} bytes): o arquivo mudou depois de aberto.", ex);
        }
    }
}
