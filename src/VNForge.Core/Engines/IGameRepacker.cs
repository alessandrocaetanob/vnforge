namespace VNForge.Core.Engines;

/// <summary>
/// O que uma engine precisa oferecer para o <c>vnf build</c> além do <see cref="IGameUnpacker"/>: devolver os scripts reconstruídos
/// aos pacotes do jogo e gravar os pacotes novos. As engines implementam esta interface além de <see cref="IEngine"/>; quem não a
/// implementa ainda não suporta o build.
/// </summary>
public interface IGameRepacker
{
    /// <summary>
    /// Para cada pacote que o <see cref="IGameUnpacker.Unpack"/> descompactou em <paramref name="unpackedDirectory"/>, grava em
    /// <paramref name="outputDirectory"/>, no mesmo caminho relativo ao jogo, o pacote com os <paramref name="rebuilt"/> no lugar dos
    /// originais. Só lê a pasta do jogo e a pasta descompactada; o que não mudou sai byte a byte igual (regra 3 do <c>CLAUDE.md</c>),
    /// e um pacote sem nenhum script alterado é copiado idêntico.
    /// </summary>
    /// <param name="gameDir">Pasta do jogo, de onde vêm os pacotes originais.</param>
    /// <param name="unpackedDirectory">Pasta que o <see cref="IGameUnpacker.Unpack"/> gerou (<c>.cache/game</c>).</param>
    /// <param name="rebuilt">Só os scripts cujo conteúdo mudou. Cada um confere se <see cref="RebuiltScript.Original"/> ainda é o conteúdo do pacote do jogo.</param>
    /// <param name="outputDirectory">Pasta de destino; o chamador a cria e a descarta se a chamada falhar.</param>
    /// <param name="originalOf">
    /// Recebe o caminho de um pacote relativo à pasta do jogo (<c>ysbin.ypf</c>, com <c>/</c>) e devolve o arquivo de onde ler o pacote original: com o jogo já
    /// injetado, o do backup da injeção, e não o traduzido que está na pasta. <c>null</c>, ou um caminho que o chamador não conhece, lê o da pasta do jogo.
    /// </param>
    /// <param name="cancellationToken">Cancela entre um pacote e outro.</param>
    /// <exception cref="VNForgeException">
    /// Um script fora de qualquer pacote descompactado, um pacote do jogo que mudou depois da extração (o original não confere), ou
    /// uma falha de leitura ou de gravação; a mensagem, em português, cita o script ou o pacote.
    /// </exception>
    GameRepackResult Repack(
        string gameDir,
        string unpackedDirectory,
        IReadOnlyList<RebuiltScript> rebuilt,
        string outputDirectory,
        Func<string, string>? originalOf = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Um script que o <c>vnf build</c> reconstruiu e a engine precisa devolver ao pacote.</summary>
/// <param name="Path">Caminho do arquivo na pasta descompactada, o mesmo de <see cref="UnpackedScript.Path"/>.</param>
/// <param name="Original">Conteúdo que o script tinha na pasta descompactada, a base da reconstrução.</param>
/// <param name="Content">Conteúdo novo.</param>
public sealed record RebuiltScript(string Path, byte[] Original, byte[] Content);

/// <summary>Um pacote que <see cref="IGameRepacker.Repack"/> gravou.</summary>
/// <param name="GamePath">Caminho do pacote relativo à pasta do jogo, com <c>/</c> (<c>ysbin.ypf</c>); é também o caminho dentro do destino.</param>
/// <param name="ReplacedFiles">Arquivos do pacote trocados por conteúdo novo; zero quando o pacote foi copiado idêntico.</param>
public sealed record RepackedArchive(string GamePath, int ReplacedFiles);

/// <summary>Resultado de <see cref="IGameRepacker.Repack"/>.</summary>
/// <param name="Archives">Pacotes gravados, na ordem em que foram gravados.</param>
public sealed record GameRepackResult(IReadOnlyList<RepackedArchive> Archives)
{
    /// <summary>Avisos de pacote que a engine pulou sem ser erro (em português). Nunca nulo.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
