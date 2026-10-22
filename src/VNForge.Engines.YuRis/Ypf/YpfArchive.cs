using VNForge.Core.Engines;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Um YPF aberto: o cabeçalho, o índice e o que o leitor detectou no arquivo. O conteúdo das entradas
/// (<see cref="OpenEntry"/>, #28) e a gravação (<see cref="WriteTo"/>, #29) ainda não existem.
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

    /// <summary>Ainda não implementado: a leitura e a descompressão das entradas são a issue #28.</summary>
    /// <exception cref="ArgumentException">A entrada pertence a outro pacote.</exception>
    /// <exception cref="NotSupportedException">Sempre, depois de validar o argumento.</exception>
    public Stream OpenEntry(ArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);
        if (!_members.Contains(entry))
        {
            throw new ArgumentException("A entrada não pertence a este pacote.", nameof(entry));
        }

        throw new NotSupportedException("Ler o conteúdo de uma entrada do YPF ainda não foi implementado (issue #28).");
    }

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
