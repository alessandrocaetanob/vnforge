namespace VNForge.Patching;

/// <summary>Quem fez a versão e o que o jogador lê; entra no manifesto do patch.</summary>
/// <param name="GameName">Nome do jogo, como o instalador mostra.</param>
/// <param name="Version">Número da versão do patch (<c>1.2.0</c>).</param>
/// <param name="PreviousVersion">Versão do patch anterior; nula na primeira.</param>
/// <param name="Credits">Linha de créditos que o instalador mostra.</param>
/// <param name="ReadMe">Texto do leia-me que o instalador oferece abrir.</param>
/// <param name="GameVersion">Versão do jogo de referência; entra no texto da janela de versão errada.</param>
/// <param name="Cover">Imagem da capa, no máximo <see cref="PatchPackage.MaxCoverBytes"/> bytes.</param>
public sealed record PatchInfo(string GameName, string Version, string? PreviousVersion, string? Credits, string? ReadMe, string? GameVersion = null, byte[]? Cover = null);

/// <summary>Um arquivo que o patch muda.</summary>
/// <param name="RelativePath">Caminho relativo à pasta do jogo, com <c>/</c>.</param>
/// <param name="OriginalPath">O arquivo original do jogo (a base do delta); nulo quando o patch cria um arquivo que o jogo não tem.</param>
/// <param name="ResultPath">O arquivo como deve ficar depois do patch (o do <c>build/</c>).</param>
public sealed record PatchSource(string RelativePath, string? OriginalPath, string ResultPath);

/// <summary>Um arquivo no manifesto do patch.</summary>
/// <param name="OriginalSha256">SHA-256 do original que o patch espera; nulo quando o arquivo é criado.</param>
/// <param name="ResultSha256">SHA-256 do arquivo depois do patch.</param>
/// <param name="Offset">Onde o delta começa, contado a partir do fim do manifesto.</param>
/// <param name="Length">Bytes do delta (comprimido) no pacote.</param>
public sealed record PatchFileEntry(
    string Path,
    string? OriginalSha256,
    long OriginalSize,
    string ResultSha256,
    long ResultSize,
    long Offset,
    long Length)
{
    /// <summary>O jogo não tem o arquivo: o pacote guarda o conteúdo novo, que não é de arquivo do jogo.</summary>
    public bool Created => OriginalSha256 is null;
}

/// <summary>O que o instalador sabe do patch sem abrir nenhum delta.</summary>
/// <param name="GameVersion">A versão do jogo para a qual o patch foi feito (<c>1.00</c>); nula nos patches que não a guardam, e o instalador então fala de "outra versão" sem número.</param>
/// <param name="Cover">A capa do jogo (PNG, JPEG ou WebP) que a janela do instalador mostra; nula quando o patch não leva capa.</param>
public sealed record PatchManifest(
    string GameName,
    string Version,
    string? PreviousVersion,
    string? Credits,
    string? ReadMe,
    IReadOnlyList<PatchFileEntry> Files,
    string? GameVersion = null,
    byte[]? Cover = null);
