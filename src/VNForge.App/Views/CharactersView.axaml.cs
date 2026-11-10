using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Views;

public sealed partial class CharactersView : UserControl
{
    /// <summary>O lado do retrato da grade: cinco colunas, como no quadro, com 20 px entre elas.</summary>
    public static readonly StyledProperty<double> CardSideProperty = AvaloniaProperty.Register<CharactersView, double>(nameof(CardSide), 160);

    private const int Columns = 5;
    private const double Gap = 20;

    public CharactersView()
    {
        InitializeComponent();
        CardsScroll.SizeChanged += (_, e) => CardSide = Math.Max(96, Math.Floor((e.NewSize.Width - 4) / Columns - Gap));
    }

    public double CardSide
    {
        get => GetValue(CardSideProperty);
        set => SetValue(CardSideProperty, value);
    }
}
