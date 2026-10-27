using VNForge.Core.Text;

namespace VNForge.Core.Engines;

/// <summary>
/// Quem sabe ler e regravar o título da janela do jogo (no YU-RIS, o <c>yscfg.ybn</c>). As engines implementam esta interface além de
/// <see cref="IGameUnpacker"/>; quem não a implementa não tem título para traduzir. O título vive num arquivo que o
/// <see cref="IGameUnpacker.Unpack"/> deixa na pasta descompactada, então o <c>vnf extract</c> o lê de lá e o <c>vnf build</c> o devolve ao
/// pacote como um <see cref="RebuiltScript"/>.
/// </summary>
public interface IGameTitleSource
{
    /// <summary>Acha e lê o título na pasta que o <see cref="IGameUnpacker.Unpack"/> gerou. Só lê.</summary>
    /// <returns>O arquivo e o título atual; nulo quando o jogo não tem o arquivo do título.</returns>
    /// <exception cref="VNForgeException">O arquivo existe mas não segue o formato; a mensagem, em português, cita o arquivo.</exception>
    GameTitleFile? ReadTitle(string unpackedDirectory);

    /// <summary>
    /// Os bytes novos do arquivo do título, com o <paramref name="title"/> codificado por <paramref name="encoder"/>. Muda só o campo do título;
    /// o resto sai como em <see cref="GameTitleFile.Content"/> (regra 3 do <c>CLAUDE.md</c>).
    /// </summary>
    /// <exception cref="VNForgeException">O título não cabe no arquivo.</exception>
    /// <exception cref="EncoderFallbackException">O <paramref name="encoder"/> não grava algum caractere.</exception>
    byte[] WithTitle(GameTitleFile file, string title, ITextEncoder encoder);
}

/// <summary>O arquivo do título lido da pasta descompactada.</summary>
/// <param name="Path">Caminho completo do arquivo na pasta descompactada, o de <see cref="RebuiltScript.Path"/>.</param>
/// <param name="Content">Bytes do arquivo como estão no jogo, a base de <see cref="IGameTitleSource.WithTitle"/> e o <see cref="RebuiltScript.Original"/>.</param>
/// <param name="Title">Título da janela decodificado.</param>
public sealed record GameTitleFile(string Path, byte[] Content, string Title);
