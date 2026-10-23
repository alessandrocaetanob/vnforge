namespace VNForge.Core.Review;

/// <summary>
/// Um formato de arquivo para revisão externa (CSV hoje; XLSX e PO entram implementando esta interface e registrando-se em
/// <see cref="ReviewFormats"/>). O contrato de importação é o mesmo para todos e fica em <see cref="ReviewImporter"/>.
/// </summary>
public interface IReviewFormat
{
    /// <summary>Nome usado em <c>--format</c>, em minúsculas (ex.: <c>csv</c>).</summary>
    string Name { get; }

    /// <summary>Extensão do arquivo, com o ponto e em minúsculas (ex.: <c>.csv</c>).</summary>
    string Extension { get; }

    /// <summary>Monta o conteúdo do arquivo.</summary>
    byte[] Write(IReadOnlyList<ReviewRow> rows);

    /// <summary>Lê o conteúdo de um arquivo.</summary>
    /// <param name="data">Bytes do arquivo.</param>
    /// <param name="path">Caminho, só para as mensagens de erro.</param>
    /// <exception cref="ReviewImportException">O arquivo não segue o formato.</exception>
    IReadOnlyList<ReviewRow> Read(byte[] data, string path);
}
