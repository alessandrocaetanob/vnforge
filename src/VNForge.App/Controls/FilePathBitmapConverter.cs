using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace VNForge.App.Controls;

/// <summary>Lê o arquivo de imagem do caminho e o devolve como <see cref="Bitmap"/> reduzido à largura da miniatura. Nulo quando o arquivo não existe ou não lê (a captura foi apagada à mão).</summary>
public sealed class FilePathBitmapConverter : IValueConverter
{
    /// <summary>A grade mostra a captura em até 600 px de largura; decodificar em 640 evita guardar na memória a imagem inteira de cada uma.</summary>
    private const int ThumbWidth = 640;

    public static FilePathBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, ThumbWidth);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
