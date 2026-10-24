using Avalonia;
using Avalonia.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class EditorView : UserControl
{
    public EditorView() => InitializeComponent();

    /// <summary>Voltar ao Editor depois de Ajustes: o perfil pode ter mudado, e a prévia da linha aberta tem de acompanhar.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as EditorViewModel)?.RefreshPreview();
    }
}
