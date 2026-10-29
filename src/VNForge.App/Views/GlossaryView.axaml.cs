using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class GlossaryView : UserControl
{
    private GlossaryViewModel? watched;
    private Control? opener;

    public GlossaryView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as GlossaryViewModel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.IsAttachedToVisualTree())
        {
            Watch(DataContext as GlossaryViewModel);
        }
    }

    /// <summary>Sair da página ou fechar a janela tira a tela antes do atraso do salvamento; grava o que estiver pendente.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as GlossaryViewModel)?.Flush();
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch(GlossaryViewModel? vm)
    {
        if (ReferenceEquals(watched, vm))
        {
            return;
        }

        if (watched is not null)
        {
            watched.PropertyChanged -= OnViewModelChanged;
        }

        watched = vm;
        if (vm is not null)
        {
            vm.PropertyChanged += OnViewModelChanged;
        }
    }

    // A ficha abre acima da tabela: o foco vai para ela e volta ao ícone que a abriu quando ela fecha.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GlossaryViewModel.Sheet) || watched is null)
        {
            return;
        }

        if (watched.Sheet is not null)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (focused is not null && opener is null && this.IsVisualAncestorOf(focused) && focused.FindAncestorOfType<CharacterSheetView>() is null)
            {
                opener = focused;
            }

            Dispatcher.UIThread.Post(
                () => this.GetVisualDescendants().OfType<CharacterSheetView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.FocusSheet(),
                DispatcherPriority.Loaded);
        }
        else if (opener is not null)
        {
            var back = opener;
            opener = null;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (back.IsEffectivelyVisible && back.IsAttachedToVisualTree())
                    {
                        back.Focus();
                    }
                },
                DispatcherPriority.Loaded);
        }
    }
}
