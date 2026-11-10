using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Views;

public sealed partial class CharactersView : UserControl
{
    /// <summary>O lado do retrato da grade: cinco colunas que dividem a largura sem sobra, como no quadro, com 20 px entre elas (o WrapPanel da tela usa o mesmo ItemSpacing).</summary>
    public static readonly StyledProperty<double> CardSideProperty = AvaloniaProperty.Register<CharactersView, double>(nameof(CardSide), 160);

    private const int Columns = 5;
    private const double Gap = 20;

    public CharactersView()
    {
        InitializeComponent();
        CardsScroll.SizeChanged += (_, e) => CardSide = Math.Max(96, (e.NewSize.Width - Gap * (Columns - 1) - 0.5) / Columns);
    }

    public double CardSide
    {
        get => GetValue(CardSideProperty);
        set => SetValue(CardSideProperty, value);
    }
}
