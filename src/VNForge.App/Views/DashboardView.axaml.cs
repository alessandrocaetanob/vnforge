using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Views;

public sealed partial class DashboardView : UserControl
{
    /// <summary>Abaixo desta largura a coluna da direita ("Precisa de você", gasto, jogo) passa para baixo do mapa, que precisa de umas 640 px para as colunas dos capítulos.</summary>
    private const double StackBelow = 1100;

    public DashboardView()
    {
        InitializeComponent();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Stack(availableSize.Width < StackBelow);
        return base.MeasureOverride(availableSize);
    }

    private void Stack(bool narrow)
    {
        if (this.FindControl<Grid>("Body") is not { } body || this.FindControl<StackPanel>("Side") is not { } side)
        {
            return;
        }

        var wanted = narrow ? 1 : 0;
        if (Grid.GetRow(side) == wanted)
        {
            return;
        }

        body.ColumnDefinitions = ColumnDefinitions.Parse(narrow ? "*" : "*,440");
        body.RowDefinitions = RowDefinitions.Parse(narrow ? "Auto,Auto" : "Auto");
        Grid.SetRow(side, wanted);
        Grid.SetColumn(side, narrow ? 0 : 1);
    }
}
