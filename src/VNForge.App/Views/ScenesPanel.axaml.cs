using Avalonia.Controls;

namespace VNForge.App.Views;

/// <summary>A lista de cenas do Editor, ligada ao <see cref="ViewModels.EditorViewModel"/>; a grade e o flyout do modo compacto usam o mesmo controle.</summary>
public sealed partial class ScenesPanel : UserControl
{
    public ScenesPanel() => InitializeComponent();
}
