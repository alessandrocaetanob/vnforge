using System.Globalization;
using Avalonia.Data.Converters;
using VNForge.App.Services;
using VNForge.Core.Glossary;

namespace VNForge.App.Controls;

/// <summary>Mostra o <see cref="CharacterGender"/> em português nos combos e células.</summary>
public sealed class GenderLabelConverter : IValueConverter
{
    public static GenderLabelConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CharacterGender gender ? ProjectLabels.Gender(gender) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
