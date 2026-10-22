using System.IO.Compression;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Um YPF aberto: o cabeçalho, o índice, o que o leitor detectou no arquivo e o conteúdo das entradas
/// (<see cref="OpenEntry"/>) e a gravação com reescrita mínima (<see cref="WriteTo"/>).
/// </summary>
public sealed class YpfArchive : IArchive
{
    private const int MaxDeflateRatio = 1032;

    private readonly Stream _stream;
    private readonly HashSet<ArchiveEntry> _members = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal YpfArchive(Stream stream, YpfHeader header, YpfParameters parameters, IReadOnlyList<YpfEntry> entries)
    {
        _stream = stream;
        Header = header;
        Parameters = parameters;
        Entries = entries;
        _members.UnionWith(entries);
    }

    /// <summary>Cabeçalho como gravado no arquivo.</summary>
    public YpfHeader Header { get; }

    /// <summary>
    /// Versão, algoritmos de hash, largura do offset, tabela de troca, XOR do nome e semântica do <c>header_size</c>
    /// detectados. A gravação reusa estes parâmetros.
    /// </summary>
    public YpfParameters Parameters { get; }

    /// <summary>Entradas na ordem do índice. Cada uma é um <see cref="YpfEntry"/>.</summary>
    public IReadOnlyList<YpfEntry> Entries { get; }

    IReadOnlyList<ArchiveEntry> IArchive.Entries => Entries;

    /// <summary>
    /// Lê os bytes armazenados da entrada, confere o <c>data_hash</c> e, se a entrada é zlib, descomprime com
    /// <see cref="ZLibStream"/>. Devolve um <see cref="MemoryStream"/> independente do fluxo de origem.
    /// </summary>
    /// <remarks>
    /// O hash só é conferido quando <see cref="YpfParameters.DataHashVerified"/> é verdadeiro: sem ele o algoritmo do
    /// arquivo não foi confirmado, e comparar com o padrão da versão poderia recusar um dado íntegro. Nesse caso o
    /// tamanho e o Adler-32 que fecha o fluxo zlib (conferido pelo ZLibStream; o truncamento é detectado aqui) ainda protegem as entradas comprimidas.
    /// </remarks>
    /// <exception cref="ArgumentException">A entrada pertence a outro pacote.</exception>
    /// <exception cref="InvalidDataException">Tamanho, hash ou fluxo zlib divergente: nunca devolve dados corrompidos.</exception>
    /// <exception cref="NotSupportedException">A entrada passa de 2 GiB e não cabe num <see cref="MemoryStream"/>.</exception>
    public Stream OpenEntry(ArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);
        if (!_members.Contains(entry))
        {
            throw new ArgumentException("A entrada não pertence a este pacote.", nameof(entry));
        }

        var ypfEntry = (YpfEntry)entry;
        if (ypfEntry.SizeStored > Array.MaxLength || ypfEntry.Size > Array.MaxLength)
        {
            throw new NotSupportedException(
                $"A entrada \"{ypfEntry.Path}\" tem {Math.Max(ypfEntry.SizeStored, ypfEntry.Size)} bytes e passa do limite de 2 GiB que o leitor carrega em memória.");
        }

        if (!ypfEntry.IsCompressed && ypfEntry.SizeStored != ypfEntry.Size)
        {
            throw Divergent(ypfEntry, $"o tamanho gravado ({ypfEntry.Size} bytes) difere dos bytes armazenados sem compressão ({ypfEntry.SizeStored}).");
        }

        var stored = ReadStored(ypfEntry);
        if (Parameters.DataHashVerified)
        {
            var actual = YpfHash.Compute(Parameters.DataHash, stored);
            if (actual != ypfEntry.DataHash)
            {
                throw Divergent(
                    ypfEntry,
                    $"o hash dos dados ({Parameters.DataHash}) é 0x{actual:X8}, mas o índice grava 0x{ypfEntry.DataHash:X8}.");
            }
        }

        return ypfEntry.IsCompressed ? new MemoryStream(Inflate(ypfEntry, stored), writable: false) : new MemoryStream(stored, writable: false);
    }

    private byte[] ReadStored(YpfEntry entry)
    {
        // Confere contra o tamanho do arquivo antes de alocar: um índice corrompido não pode pedir gigabytes.
        if (entry.Offset > (ulong)_stream.Length || entry.SizeStored > (ulong)_stream.Length - entry.Offset)
        {
            throw new InvalidDataException(
                $"Os dados da entrada \"{entry.Path}\" ({entry.SizeStored} bytes no offset {entry.Offset}) passam do fim do arquivo.");
        }

        var stored = new byte[entry.SizeStored];
        try
        {
            _stream.Seek((long)entry.Offset, SeekOrigin.Begin);
            _stream.ReadExactly(stored);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                $"Os dados da entrada \"{entry.Path}\" ({entry.SizeStored} bytes no offset {entry.Offset}) passam do fim do arquivo.",
                ex);
        }

        return stored;
    }

    private static byte[] Inflate(YpfEntry entry, byte[] stored)
    {
        if (entry.Size > (long)entry.SizeStored * MaxDeflateRatio)
        {
            throw Divergent(entry, $"tamanho descomprimido impossível para {entry.SizeStored} bytes comprimidos (o deflate não passa de {MaxDeflateRatio} para 1): o índice grava {entry.Size}.");
        }

        var content = new byte[entry.Size];
        var source = new EofTrackingStream(new MemoryStream(stored, writable: false));
        bool hasExtra;
        try
        {
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            zlib.ReadExactly(content);
            hasExtra = zlib.ReadByte() != -1;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                $"A entrada \"{entry.Path}\" diverge: o fluxo zlib termina antes dos {entry.Size} bytes esperados.",
                ex);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(
                $"O fluxo zlib da entrada \"{entry.Path}\" está corrompido (cabeçalho, dados ou Adler-32 do fim inválido).",
                ex);
        }

        if (hasExtra)
        {
            throw Divergent(entry, $"o tamanho descomprimido é maior que os {entry.Size} bytes gravados no índice.");
        }

        // O ZLibStream do .NET devolve fim de fluxo, sem lançar, quando a entrada acaba no meio do trailer. Se o
        // inflater pediu mais bytes e bateu no fim, o fluxo está truncado; bytes de preenchimento depois do trailer
        // completo não são lidos e passam (GARbro ArcYPF.cs:155-157, ypf.py, Xorboth Util.cs:42-59 também toleram).
        if (source.HitEof)
        {
            throw Divergent(entry, "o fluxo zlib está truncado: termina antes do Adler-32 do fim.");
        }

        return content;
    }

    private sealed class EofTrackingStream(Stream inner) : Stream
    {
        public bool HitEof { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            if (read == 0 && !buffer.IsEmpty)
            {
                HitEof = true;
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static InvalidDataException Divergent(YpfEntry entry, string reason) =>
        new($"A entrada \"{entry.Path}\" diverge: {reason}");

    /// <summary>
    /// Grava um YPF novo com reescrita mínima: cabeçalho (inclusive o padding), índice, lacunas e trailer saem do
    /// original; entradas não trocadas são copiadas cruas, na mesma ordem, sem recomprimir nem recalcular hash.
    /// Só a entrada trocada é recomprimida (se o original era zlib) e tem o <c>data_hash</c> recalculado com o
    /// algoritmo detectado em <see cref="YpfParameters.DataHash"/>. Offsets e tamanhos são recalculados.
    /// </summary>
    /// <remarks>
    /// Uma troca com o mesmo conteúdo da entrada conta como não trocada (o zlib não reproduz os bytes do original).
    /// Quando <see cref="YpfParameters.DataHashVerified"/> é falso, o algoritmo do hash dos dados deste arquivo é
    /// desconhecido, e o escritor recusa qualquer troca em vez de gravar um hash do padrão da versão que pode estar
    /// errado; o pacote sem trocas continua copiável. O destino é gravado a partir da posição atual.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Chave inexistente, conteúdo nulo, destino sem gravação ou busca, ou destino igual ao fluxo de origem
    /// (o mesmo objeto, ou um <see cref="FileStream"/> no mesmo caminho). Lançada antes de gravar qualquer byte.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Troca num pacote sem algoritmo de hash confirmado, com blocos de dados parcialmente sobrepostos, ou que
    /// passaria dos 4 GiB de um offset de 32 bits. Lançada antes de gravar qualquer byte.
    /// </exception>
    public void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(replacements);
        if (!output.CanWrite || !output.CanSeek)
        {
            throw new ArgumentException("O destino precisa permitir gravação e busca (seekable).", nameof(output));
        }

        if (IsSourceStream(output))
        {
            throw new ArgumentException(
                "O destino não pode ser o arquivo de origem do pacote: grave num arquivo novo e deixe o fluxo de injeção, com backup, trocar o original.",
                nameof(output));
        }

        new YpfWriter(this, _stream).Write(output, replacements);
    }

    private bool IsSourceStream(Stream output)
    {
        if (ReferenceEquals(output, _stream))
        {
            return true;
        }

        if (output is not FileStream destination || _stream is not FileStream origin)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(destination.Name), Path.GetFullPath(origin.Name), comparison);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }
}
