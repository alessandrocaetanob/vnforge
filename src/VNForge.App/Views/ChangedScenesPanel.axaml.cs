using Avalonia.Controls;

namespace VNForge.App.Views;

/// <summary>A lista "Mudou desde o último teste", ligada ao <see cref="ViewModels.ChangedScenesViewModel"/>; a página Testar e o Painel usam o mesmo controle.</summary>
public sealed partial class ChangedScenesPanel : UserControl
{
    public ChangedScenesPanel() => InitializeComponent();
}
