using System.IO.Compression;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Um YPF aberto: o cabeçalho, o índice, o que o leitor detectou no arquivo e o conteúdo das entradas
/// (<see cref="OpenEntry"/>). A gravação (<see cref="WriteTo"/>, #29) ainda não existe.
/// </summary>
public sealed class YpfArchive : IArchive
{
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
    /// detectados. A gravação (#29) reusa estes parâmetros.
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
    /// tamanho e o próprio zlib (que traz um Adler-32 no fim) ainda protegem as entradas comprimidas.
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
        var stored = new byte[entry.SizeStored];
        try
        {
            _stream.Seek(checked((long)entry.Offset), SeekOrigin.Begin);
            _stream.ReadExactly(stored);
        }
        catch (Exception ex) when (ex is EndOfStreamException or OverflowException)
        {
            throw new InvalidDataException(
                $"Os dados da entrada \"{entry.Path}\" ({entry.SizeStored} bytes no offset {entry.Offset}) passam do fim do arquivo.",
                ex);
        }

        return stored;
    }

    private static byte[] Inflate(YpfEntry entry, byte[] stored)
    {
        var content = new byte[entry.Size];
        bool hasExtra;
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(stored, writable: false), CompressionMode.Decompress);
            zlib.ReadExactly(content);
            hasExtra = zlib.ReadByte() != -1;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                $"A entrada \"{entry.Path}\" diverge: o tamanho descomprimido é menor que os {entry.Size} bytes gravados no índice.",
                ex);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"O fluxo zlib da entrada \"{entry.Path}\" é inválido: {ex.Message}", ex);
        }

        if (hasExtra)
        {
            throw Divergent(entry, $"o tamanho descomprimido é maior que os {entry.Size} bytes gravados no índice.");
        }

        return content;
    }

    private static InvalidDataException Divergent(YpfEntry entry, string reason) =>
        new($"A entrada \"{entry.Path}\" diverge: {reason}");

    /// <summary>Ainda não implementado: a gravação com reescrita mínima é a issue #29.</summary>
    /// <exception cref="NotSupportedException">Sempre, depois de validar os argumentos.</exception>
    public void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(replacements);
        throw new NotSupportedException("Gravar um YPF ainda não foi implementado (issue #29).");
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
