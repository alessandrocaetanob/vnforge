namespace VNForge.Core.Engines;

/// <summary>
/// Um pacote aberto por <see cref="IArchiveFormat.Open"/>.
/// </summary>
/// <remarks>
/// Uma instância não é segura para chamadas concorrentes: <see cref="OpenEntry"/> e <see cref="WriteTo"/>
/// leem o mesmo fluxo de origem. Para ler em paralelo, abra um <see cref="IArchive"/> por thread.
/// </remarks>
public interface IArchive : IDisposable
{
    /// <summary>
    /// Entradas do pacote, na ordem em que estão no arquivo. <see cref="ArchiveEntry.Path"/> é único:
    /// <see cref="IArchiveFormat.Open"/> lança <see cref="InvalidFileException"/> se houver duplicata.
    /// </summary>
    IReadOnlyList<ArchiveEntry> Entries { get; }

    /// <summary>
    /// Devolve o conteúdo já descomprimido de uma entrada num fluxo independente: legível, com busca (seekable),
    /// sem posição compartilhada com o fluxo de origem e ainda válido depois do <see cref="IDisposable.Dispose"/>
    /// do pacote (por exemplo, um <see cref="MemoryStream"/>). Quem chama descarta o fluxo.
    /// </summary>
    /// <remarks>
    /// O tamanho e o hash dos dados são conferidos, e a divergência lança <see cref="InvalidDataException"/> em vez
    /// de devolver dados corrompidos. A verificação é do formato: o algoritmo de hash por versão ainda não está
    /// validado contra todos os jogos (o doc 06 marca como não testado, e há ambiguidade nas versões 477 e 478).
    /// </remarks>
    /// <param name="entry">Entrada de <see cref="Entries"/> deste pacote.</param>
    /// <exception cref="ArgumentException">A entrada pertence a outro pacote.</exception>
    /// <exception cref="InvalidDataException">Tamanho ou hash dos dados divergente.</exception>
    Stream OpenEntry(ArchiveEntry entry);

    /// <summary>
    /// Grava um pacote novo em <paramref name="output"/> com reescrita mínima: mantém versão, ordem e
    /// parâmetros do original, copia crua, byte a byte, toda entrada que não foi trocada e recomprime só as trocadas.
    /// Sem trocas o resultado é idêntico ao original (regra 3 do <c>CLAUDE.md</c>).
    /// </summary>
    /// <remarks>
    /// Uma troca cujo conteúdo é byte a byte igual ao conteúdo descomprimido da entrada é tratada como não trocada
    /// e copiada crua, porque o zlib não reproduz os bytes comprimidos do original. Assim, um <c>pack</c> que
    /// passa todos os arquivos da pasta ainda gera um arquivo idêntico quando nada mudou. A entrada trocada
    /// mantém o <see cref="ArchiveEntry.IsCompressed"/> da original.
    /// </remarks>
    /// <param name="output">Destino gravável e com busca (seekable), para o escritor acertar offsets e cabeçalho. Nunca o fluxo de origem do pacote.</param>
    /// <param name="replacements">Novo conteúdo por <see cref="ArchiveEntry.Path"/>.</param>
    /// <exception cref="ArgumentException">
    /// Uma chave de <paramref name="replacements"/> não existe no pacote, ou <paramref name="output"/> não é gravável
    /// e seekable. Lançada antes de gravar qualquer byte.
    /// </exception>
    void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements);
}

/// <summary>
/// Uma entrada de um pacote. Não é selada: cada formato deriva dela para guardar os campos internos que precisa
/// na reescrita (por exemplo, o hash e os bytes do nome, o tipo de arquivo e o offset do YPF).
/// </summary>
/// <param name="Path">Caminho interno, com <c>/</c> como separador. Único no pacote.</param>
/// <param name="Size">Tamanho do conteúdo descomprimido, em bytes.</param>
/// <param name="IsCompressed">Verdadeiro quando a entrada está comprimida no pacote.</param>
public record ArchiveEntry(string Path, long Size, bool IsCompressed);
