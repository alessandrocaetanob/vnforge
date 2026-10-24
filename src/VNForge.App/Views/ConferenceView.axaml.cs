using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class ConferenceView : UserControl
{
    public ConferenceView() => InitializeComponent();

    /// <summary>Duplo clique na ocorrência abre a linha no Editor, igual ao botão "Abrir linha". Duplo clique num botão da linha não conta.</summary>
    private void OnIssueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (sender is Control { DataContext: ConferenceIssueRowViewModel row } && DataContext is ConferenceViewModel vm && vm.OpenLineCommand.CanExecute(row))
        {
            vm.OpenLineCommand.Execute(row);
            e.Handled = true;
        }
    }
}
