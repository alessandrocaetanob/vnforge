using Avalonia;
using Avalonia.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Controls;

/// <summary>
/// Os três cartões de acentos do Novo projeto e dos Ajustes: o mesmo controle e o mesmo <see cref="AccentChoices"/> nos dois lugares.
/// Cada cartão é um <c>RadioButton</c> ligado a <see cref="ChoiceOption.IsSelected"/>; a seleção única fica no <see cref="ChoiceGroup"/> da tela.
/// </summary>
public sealed partial class AccentCards : UserControl
{
    public static readonly StyledProperty<IEnumerable<ChoiceOption>?> OptionsProperty =
        AvaloniaProperty.Register<AccentCards, IEnumerable<ChoiceOption>?>(nameof(Options));

    public static readonly StyledProperty<string> GroupNameProperty =
        AvaloniaProperty.Register<AccentCards, string>(nameof(GroupName), "Accents");

    public AccentCards() => InitializeComponent();

    public IEnumerable<ChoiceOption>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Nome do grupo dos <c>RadioButton</c>; cada tela usa o seu para as setas do teclado não saltarem entre grupos.</summary>
    public string GroupName
    {
        get => GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }
}
