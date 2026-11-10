using Avalonia.Controls;
using Avalonia.Input;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class ConferenceGroupView : UserControl
{
    public ConferenceGroupView() => InitializeComponent();

    /// <summary>Marcar a bolinha de uma tradução faz o mesmo que "Usar em todas" nela; a da mais usada já vem marcada e não faz nada.</summary>
    private void OnRadioTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ConferenceVariantViewModel { IsReference: false } variant } && variant.Card.UseCommand.CanExecute(variant))
        {
            variant.Card.UseCommand.Execute(variant);
        }
    }
}
