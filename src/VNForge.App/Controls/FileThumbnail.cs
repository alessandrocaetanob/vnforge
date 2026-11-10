using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace VNForge.App.Controls;

/// <summary>
/// A miniatura de um arquivo de imagem (as capturas da aba Jogar). Decodifica fora da thread da tela, reduzida à largura da grade, e guarda o resultado por caminho:
/// com dezenas de capturas de 1080p a janela não trava e voltar à aba não decodifica de novo. Sem o arquivo, ou se ele não lê (apagado à mão), fica vazio.
/// </summary>
public sealed class FileThumbnail : Image
{
    public static readonly StyledProperty<string?> FilePathProperty = AvaloniaProperty.Register<FileThumbnail, string?>(nameof(FilePath));

    /// <summary>A grade mostra a captura em até 600 px de largura.</summary>
    private const int ThumbWidth = 480;

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    static FileThumbnail() => FilePathProperty.Changed.AddClassHandler<FileThumbnail>((thumbnail, e) => _ = thumbnail.LoadAsync(e.NewValue as string));

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private async Task LoadAsync(string? path)
    {
        Source = null;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var bitmap = await Cache.GetOrAdd(path, key => Task.Run(() => Decode(key)));
        if (FilePath == path)
        {
            Source = bitmap;
        }
    }

    private static Bitmap? Decode(string path)
    {
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
}
