namespace VNForge.Core.Engines;

/// <summary>Um pacote aberto por <see cref="IArchiveFormat.Open"/>.</summary>
public interface IArchive : IDisposable
{
    /// <summary>Entradas do pacote, na ordem em que estão no arquivo.</summary>
    IReadOnlyList<ArchiveEntry> Entries { get; }

    /// <summary>
    /// Abre o conteúdo já descomprimido de uma entrada.
    /// O tamanho e o hash dos dados são conferidos; divergência lança <see cref="InvalidDataException"/> em vez de devolver dados corrompidos.
    /// </summary>
    /// <param name="entry">Entrada de <see cref="Entries"/>.</param>
    Stream OpenEntry(ArchiveEntry entry);

    /// <summary>
    /// Grava um pacote novo em <paramref name="output"/> com reescrita mínima: mantém versão, ordem e
    /// parâmetros do original, copia byte a byte as entradas que não estão em <paramref name="replacements"/>
    /// e recomprime só as trocadas. Sem trocas, o resultado é idêntico ao original.
    /// </summary>
    /// <param name="output">Destino. Nunca o fluxo de origem do pacote.</param>
    /// <param name="replacements">Novo conteúdo por <see cref="ArchiveEntry.Path"/>; um caminho que não existe no pacote é erro.</param>
    void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements);
}

/// <summary>Uma entrada de um pacote.</summary>
/// <param name="Path">Caminho interno, com <c>/</c> como separador.</param>
/// <param name="Size">Tamanho do conteúdo descomprimido, em bytes.</param>
/// <param name="IsCompressed">Verdadeiro quando a entrada está comprimida no pacote.</param>
public sealed record ArchiveEntry(string Path, long Size, bool IsCompressed);
