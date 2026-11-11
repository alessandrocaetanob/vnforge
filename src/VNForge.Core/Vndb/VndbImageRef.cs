using System.Text.Json.Serialization;
using VNForge.Core.Library;

namespace VNForge.Core.Vndb;

/// <summary>
/// A referência a uma imagem do VNDB que o app mostra: o endereço e as marcas de conteúdo, para que a privacidade decida antes de baixar. É o que
/// <c>vndb.json</c> e <c>characters.json</c> guardam, no lugar dos bytes. <paramref name="Sexual"/> e <paramref name="Violence"/> são as médias de 0 a 2 do VNDB.
/// </summary>
public sealed record VndbImageRef([property: JsonRequired] string Url, double Sexual = 0, double Violence = 0)
{
    /// <summary>A referência da capa: a miniatura quando o VNDB tem, que basta para as telas e pesa menos; nula sem imagem.</summary>
    public static VndbImageRef? Cover(VndbImage? image) =>
        image is null ? null : new VndbImageRef(string.IsNullOrEmpty(image.ThumbnailUrl) ? image.Url : image.ThumbnailUrl, image.Sexual, image.Violence);

    /// <summary>A referência do retrato: a imagem inteira (o VNDB não dá miniatura de personagem); nula sem imagem.</summary>
    public static VndbImageRef? Portrait(VndbImage? image) =>
        image is null ? null : new VndbImageRef(image.Url, image.Sexual, image.Violence);
}

/// <summary>
/// Quando uma imagem do VNDB pode ser mostrada e de onde pode ser baixada. A privacidade é a da Biblioteca (<see cref="LibraryPrivacy"/>): o modo adulto vale para as duas marcas,
/// a sexual e a de violência.
/// </summary>
public static class VndbImageVisibility
{
    /// <summary>A partir desta média de votos (0 a 2) a imagem conta como adulta. A Biblioteca usa o mesmo corte para marcar a capa do jogo.</summary>
    public const double AdultLevel = 1.0;

    /// <summary>A imagem é sexual ou violenta: a média de votos (0 a 2) passa de <see cref="AdultLevel"/>.</summary>
    public static bool IsAdult(VndbImageRef image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.Sexual >= AdultLevel || image.Violence >= AdultLevel;
    }

    /// <summary>A privacidade não deixa ver a imagem como ela vem: ela é adulta e <see cref="LibraryPrivacy.ShowAdult"/> é falso (borrar ou esconder).</summary>
    public static bool IsHidden(VndbImageRef image, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(privacy);
        return !privacy.ShowAdult && IsAdult(image);
    }

    /// <summary>A tela mostra a imagem borrada: ela é adulta e o modo é <see cref="AdultMode.Blur"/>.</summary>
    public static bool IsBlurred(VndbImageRef image, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(privacy);
        return privacy.Adult == AdultMode.Blur && IsAdult(image);
    }

    /// <summary>O app pode baixar e guardar a imagem: tudo que a privacidade deixa ver, inclusive a adulta que a tela vai borrar. Só o <see cref="AdultMode.Hide"/> a barra.</summary>
    public static bool MayLoad(VndbImageRef image, LibraryPrivacy privacy) => !IsHidden(image, privacy) || IsBlurred(image, privacy);

    /// <summary>
    /// O endereço é de <c>vndb.org</c> (ou de um subdomínio, como <c>t.vndb.org</c>) por HTTPS. <c>vndb.json</c> e <c>characters.json</c> são editáveis à mão, então
    /// o app não baixa de endereço nenhum que esteja neles.
    /// </summary>
    public static bool IsAllowedSource(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host == "vndb.org" || uri.Host.EndsWith(".vndb.org", StringComparison.OrdinalIgnoreCase);
    }
}
