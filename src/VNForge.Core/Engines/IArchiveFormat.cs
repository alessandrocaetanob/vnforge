namespace VNForge.Core.Engines;

/// <summary>
/// Um formato de pacote de arquivos do jogo (ex.: YPF). Abre pacotes para leitura e os reescreve
/// com reescrita mínima, ou seja, entradas que não mudaram saem byte a byte iguais.
/// </summary>
public interface IArchiveFormat
{
    /// <summary>Identificador estável do formato, em minúsculas (ex.: <c>"ypf"</c>).</summary>
    string Id { get; }

    /// <summary>Extensões comuns do formato, com ponto e em minúsculas (ex.: <c>".ypf"</c>).</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Confere, sem lançar exceção, se o conteúdo do fluxo parece ser deste formato, lendo a partir do início.
    /// A posição do fluxo volta ao valor original.
    /// </summary>
    /// <param name="stream">Fluxo legível e com busca (seekable).</param>
    bool CanOpen(Stream stream);

    /// <summary>
    /// Lê o cabeçalho e o índice do pacote, a partir do início do fluxo.
    /// O <see cref="IArchive"/> devolvido passa a ser o dono do fluxo: descartá-lo fecha o fluxo.
    /// Se a chamada lançar exceção, o fluxo continua sendo de quem chamou.
    /// </summary>
    /// <param name="stream">Fluxo legível e com busca (seekable).</param>
    /// <exception cref="InvalidDataException">
    /// O conteúdo não é do formato, o índice é inconsistente ou há dois <see cref="ArchiveEntry.Path"/> iguais;
    /// a mensagem, em português, diz o que falhou.
    /// </exception>
    IArchive Open(Stream stream);
}
