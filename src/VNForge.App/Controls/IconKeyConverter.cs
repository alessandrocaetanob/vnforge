using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace VNForge.App.Controls;

/// <summary>Troca a chave de um ícone (<c>Icon.Home</c>) pelo <c>StreamGeometry</c> de <c>Assets/Icons.axaml</c>.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static IconKeyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var geometry) ? geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
