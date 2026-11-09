using System.Text.Json.Serialization;

namespace VNForge.Core.Images;

/// <summary>Retângulo em pixels da imagem, com origem no canto superior esquerdo.</summary>
public sealed record ImageRegion(
    [property: JsonRequired] int X,
    [property: JsonRequired] int Y,
    [property: JsonRequired] int Width,
    [property: JsonRequired] int Height)
{
    /// <summary>A interseção com uma imagem de <paramref name="width"/> por <paramref name="height"/>; vazia quando a região cai fora.</summary>
    public ImageRegion ClipTo(int width, int height)
    {
        var x0 = Math.Clamp(X, 0, width);
        var y0 = Math.Clamp(Y, 0, height);
        var x1 = Math.Clamp(X + Width, 0, width);
        var y1 = Math.Clamp(Y + Height, 0, height);
        return new ImageRegion(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }
}

/// <summary>Como a área do texto original é apagada (<see cref="ImageEdit.Erase"/>).</summary>
public static class ImageEraseModes
{
    /// <summary>Preenche a área com a interpolação das cores da borda ao redor, o que acompanha degradês.</summary>
    public const string Surrounding = "surrounding";

    /// <summary>Preenche a área com uma cor lisa (<see cref="ImageEdit.EraseColor"/>).</summary>
    public const string Solid = "solid";

    public static bool IsValid(string? mode) => mode is Surrounding or Solid;
}

/// <summary>
/// A edição de uma imagem do jogo (#535): a área do texto original que é apagada e o texto da tradução escrito no lugar, com a fonte escolhida.
/// Vive em <c>image-edits.json</c>, uma edição por <see cref="Archive"/> e <see cref="Path"/>; a imagem já pronta fica em <c>images/</c>
/// (<see cref="Projects.ProjectFolder.EditedImagePath"/>) e é ela que o build grava no pacote.
/// </summary>
/// <param name="Archive">Pacote do jogo, relativo à pasta do jogo e com <c>/</c> (<c>img.ypf</c>).</param>
/// <param name="Path">Caminho da imagem dentro do pacote, com <c>/</c>.</param>
/// <param name="Hash">SHA-256 da imagem original, o mesmo de <see cref="ImageTextEntry.Hash"/>. Se o pacote do jogo mudar a imagem, a edição deixa de valer.</param>
/// <param name="Width">Largura da imagem original, em pixels.</param>
/// <param name="Height">Altura da imagem original, em pixels.</param>
/// <param name="Region">A área que o texto original ocupa; é apagada e é onde o texto novo é centralizado.</param>
/// <param name="Erase">Um de <see cref="ImageEraseModes"/>.</param>
/// <param name="EraseColor">Cor da área apagada em <see cref="ImageEraseModes.Solid"/>, como <c>#rrggbb</c>; ignorada nos outros modos.</param>
/// <param name="Text">O texto da tradução; vazio só apaga a área.</param>
/// <param name="Font">Nome da família da fonte.</param>
/// <param name="FontSize">Tamanho da fonte, em pixels da imagem.</param>
/// <param name="Bold">Verdadeiro para negrito.</param>
/// <param name="Color">Cor do texto, como <c>#rrggbb</c>.</param>
/// <param name="Outline">Cor do contorno do texto, como <c>#rrggbb</c>; <c>null</c> sem contorno.</param>
/// <param name="Marked">Verdadeiro quando a edição entra no build; só as marcadas são gravadas no YPF.</param>
public sealed record ImageEdit(
    [property: JsonRequired] string Archive,
    [property: JsonRequired] string Path,
    [property: JsonRequired] string Hash,
    [property: JsonRequired] int Width,
    [property: JsonRequired] int Height,
    [property: JsonRequired] ImageRegion Region,
    [property: JsonRequired] string Erase,
    string? EraseColor,
    [property: JsonRequired] string Text,
    [property: JsonRequired] string Font,
    [property: JsonRequired] double FontSize,
    [property: JsonRequired] bool Bold,
    [property: JsonRequired] string Color,
    string? Outline,
    [property: JsonRequired] bool Marked);
