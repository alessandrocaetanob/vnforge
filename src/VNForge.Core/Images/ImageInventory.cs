using System.Text.Json.Serialization;

namespace VNForge.Core.Images;

/// <summary>
/// Uma imagem do jogo que o modelo já leu (#534): onde ela está, qual é o conteúdo (<see cref="Hash"/>) e o texto em japonês que ele transcreveu.
/// Vive em <c>images.json</c>, uma entrada por <see cref="Archive"/> e <see cref="Path"/>.
/// </summary>
/// <param name="Archive">Pacote do jogo, relativo à pasta do jogo e com <c>/</c> (<c>ysbin.ypf</c>).</param>
/// <param name="Path">Caminho da imagem dentro do pacote, com <c>/</c>.</param>
/// <param name="Hash">SHA-256 do conteúdo descomprimido, em hexadecimal minúsculo. Duas entradas com o mesmo hash são a mesma imagem e dividem a leitura.</param>
/// <param name="Text">O texto lido; vazio quando a imagem não tem texto em japonês.</param>
public sealed record ImageTextEntry(
    [property: JsonRequired] string Archive,
    [property: JsonRequired] string Path,
    [property: JsonRequired] string Hash,
    [property: JsonRequired] int Width,
    [property: JsonRequired] int Height,
    [property: JsonRequired] string Text)
{
    [JsonIgnore]
    public bool HasText => Text.Length > 0;
}
