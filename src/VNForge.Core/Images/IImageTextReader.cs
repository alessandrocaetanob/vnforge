namespace VNForge.Core.Images;

/// <summary>Uma imagem pronta para o leitor.</summary>
/// <param name="Name">Identifica a imagem nas mensagens (<c>pacote/caminho</c>).</param>
/// <param name="MediaType">Um dos tipos que a API aceita: <c>image/png</c>, <c>image/jpeg</c>, <c>image/gif</c> ou <c>image/webp</c>.</param>
/// <param name="Data">Os bytes do arquivo, como estão no pacote.</param>
public sealed record ImageToRead(string Name, string MediaType, byte[] Data);

/// <summary>O que o leitor viu numa imagem.</summary>
/// <param name="Text">O texto em japonês transcrito, com quebra de linha entre as linhas da imagem; vazio quando não há texto.</param>
/// <param name="Refused">O modelo recusou ler esta imagem; <paramref name="Text"/> não vale.</param>
/// <param name="InputTokens">Tokens de entrada que a chamada gastou, para o custo real; zero quando o leitor não os conta.</param>
public sealed record ImageTextReading(string Text, bool Refused = false, long InputTokens = 0, long OutputTokens = 0);

/// <summary>
/// Fronteira entre o inventário de imagens e a IA: diz se uma imagem tem texto em japonês e qual é. O provedor Claude (visão) e o
/// <see cref="FakeImageTextReader"/> dos testes a implementam. Falha de rede ou de formato é exceção; recusa do modelo é <see cref="ImageTextReading.Refused"/>.
/// </summary>
public interface IImageTextReader
{
    Task<ImageTextReading> ReadAsync(ImageToRead image, CancellationToken ct);
}
