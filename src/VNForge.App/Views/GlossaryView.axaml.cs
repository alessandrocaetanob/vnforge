using Avalonia;
using Avalonia.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class GlossaryView : UserControl
{
    public GlossaryView()
    {
        InitializeComponent();
    }

    /// <summary>Sair da página ou fechar a janela tira a tela antes do atraso do salvamento; grava o que estiver pendente.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as GlossaryViewModel)?.Flush();
        base.OnDetachedFromVisualTree(e);
    }
}
