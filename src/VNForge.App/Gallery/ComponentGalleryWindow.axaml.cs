using Avalonia.Controls;

namespace VNForge.App.Gallery;

/// <summary>Abre com <c>dotnet run --project src/VNForge.App -- --gallery</c>. Só existe no build Debug.</summary>
public sealed partial class ComponentGalleryWindow : Window
{
    public const string Flag = "--gallery";

    public ComponentGalleryWindow() => InitializeComponent();
}
