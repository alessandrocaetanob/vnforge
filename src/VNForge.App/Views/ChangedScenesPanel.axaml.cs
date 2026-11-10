using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Views;

/// <summary>A lista "Mudou desde o último teste", ligada ao <see cref="ViewModels.ChangedScenesViewModel"/>; a página Testar e o Painel usam o mesmo controle.</summary>
public sealed partial class ChangedScenesPanel : UserControl
{
    /// <summary>O espaço entre o título, a frase e as linhas; a página Testar o fecha para casar com o cartão do quadro.</summary>
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<ChangedScenesPanel, double>(nameof(Spacing), 6);

    static ChangedScenesPanel() =>
        SpacingProperty.Changed.AddClassHandler<ChangedScenesPanel>((panel, change) => panel.Root.Spacing = (double)change.NewValue!);

    public ChangedScenesPanel() => InitializeComponent();

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
}
