using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace VNForge.Installer.Views;

/// <summary>Os bytes da capa (PNG, JPEG ou WebP) viram a imagem da janela; bytes que não decodificam dão nenhuma imagem, e a capa desenhada fica por baixo.</summary>
public sealed class BytesToBitmapConverter : IValueConverter
{
    public static BytesToBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
