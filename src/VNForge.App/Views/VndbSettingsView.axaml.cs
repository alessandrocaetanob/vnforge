using Avalonia.Controls;
using Avalonia.Interactivity;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>A aba VNDB de Ajustes; a lógica está em <see cref="VndbConnectionViewModel"/>.</summary>
public partial class VndbSettingsView : UserControl
{
    public VndbSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Ao abrir a aba, o app pergunta ao VNDB de quem é o token e o que ele permite.</summary>
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is VndbConnectionViewModel vm)
        {
            await vm.RefreshAsync();
        }
    }
}
