using Avalonia.Styling;

namespace VNForge.App.Services;

public static class ThemeChoiceExtensions
{
    /// <summary>Sistema vira <see cref="ThemeVariant.Default"/>, que segue o tema do Windows.</summary>
    public static ThemeVariant ToVariant(this ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => ThemeVariant.Light,
        ThemeChoice.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
