namespace VNForge.Core.Engines;

/// <summary>
/// O que uma engine precisa oferecer para o <c>vnf extract</c>: descompactar os pacotes do jogo que carregam os scripts e
/// abrir o resultado, entregando os scripts e o <see cref="EngineContext"/> da engine. As engines implementam esta interface
/// além de <see cref="IEngine"/>; quem não a implementa ainda não suporta a extração.
/// </summary>
/// <remarks>
/// A separação entre <see cref="Unpack"/> e <see cref="Open"/> existe para o <c>vnf build</c> reabrir a pasta que o
/// <c>vnf extract</c> deixou em <c>.cache/</c> sem tocar o jogo de novo.
/// </remarks>
public interface IGameUnpacker
{
    /// <summary>
    /// Descompacta para <paramref name="destinationDirectory"/> os pacotes de <paramref name="gameDir"/> que a engine precisa para ler
    /// os scripts. Só lê a pasta do jogo (regra 2 do <c>CLAUDE.md</c>); arquivos que já existem no destino são sobrescritos.
    /// Cada pacote vai para uma subpasta cujo caminho é o do pacote relativo ao jogo, com <c>/</c> (por exemplo <c>pac/ysbin.ypf</c>).
    /// </summary>
    /// <param name="gameDir">Pasta do jogo.</param>
    /// <param name="destinationDirectory">Pasta de destino, normalmente <c>.cache/game</c> do projeto.</param>
    /// <param name="cancellationToken">Cancela entre um arquivo e outro.</param>
    /// <exception cref="VNForgeException">Pasta inexistente, nenhum pacote com scripts, ou pacote corrompido; a mensagem está em português.</exception>
    GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre uma pasta já descompactada por <see cref="Unpack"/>: acha os scripts, carrega o contexto da engine e os nomes legíveis.
    /// Não grava nada.
    /// </summary>
    /// <exception cref="VNForgeException">A pasta não tem os arquivos que a engine espera (por exemplo, o <c>ysc.ybn</c> do YU-RIS) ou eles estão inválidos.</exception>
    UnpackedGame Open(string unpackedDirectory);
}

/// <summary>Um pacote do jogo que <see cref="IGameUnpacker.Unpack"/> descompactou.</summary>
/// <param name="GamePath">Caminho do pacote relativo à pasta do jogo, com <c>/</c> (<c>ysbin.ypf</c>).</param>
/// <param name="Directory">Subpasta do destino que recebeu o conteúdo, relativa ao destino, com <c>/</c>.</param>
/// <param name="FileCount">Arquivos gravados.</param>
/// <param name="ByteCount">Soma dos tamanhos descomprimidos.</param>
public sealed record UnpackedArchive(string GamePath, string Directory, int FileCount, long ByteCount);

/// <summary>Resultado de <see cref="IGameUnpacker.Unpack"/>.</summary>
/// <param name="Archives">Pacotes descompactados, na ordem em que foram gravados.</param>
public sealed record GameUnpackResult(IReadOnlyList<UnpackedArchive> Archives)
{
    /// <summary>Avisos de pacote ou pasta que a engine pulou sem ser erro (em português). Nunca nulo.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Um script achado numa pasta descompactada.</summary>
/// <param name="Name">Nome do arquivo (<c>yst00012.ybn</c>), de onde sai a chave do script (<see cref="ScriptFile.Key"/>).</param>
/// <param name="Path">Caminho completo do arquivo na pasta descompactada.</param>
/// <param name="Format">Formato que lê e reconstrói o script.</param>
/// <param name="DisplayName">Nome legível que a engine conhece (no YU-RIS, o do <c>yst_list.ybn</c>), ou nulo.</param>
public sealed record UnpackedScript(string Name, string Path, IScriptFormat Format, string? DisplayName)
{
    /// <summary>Pasta do script no jogo, com <c>/</c> (no YU-RIS, a do caminho na <c>yst_list.ybn</c>, como <c>userscript/01_cap1</c>), ou nulo quando a engine não a conhece. Os capítulos do Painel saem dela.</summary>
    public string? Folder { get; init; }
}

/// <summary>Scripts e contexto de uma pasta descompactada (<see cref="IGameUnpacker.Open"/>).</summary>
/// <param name="Context">Contexto da engine para <see cref="IScriptFormat.Extract"/> e <see cref="IScriptFormat.Rebuild"/>.</param>
/// <param name="Scripts">Scripts ordenados pelo nome, sem diferenciar caixa.</param>
public sealed record UnpackedGame(EngineContext Context, IReadOnlyList<UnpackedScript> Scripts)
{
    /// <summary>Avisos de arquivo opcional que não pôde ser lido (por exemplo, nomes legíveis). Nunca nulo.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
