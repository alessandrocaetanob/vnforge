using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class EvalView : UserControl
{
    /// <summary>Abaixo desta largura o gráfico e a leitura passam para baixo da tabela, que precisa de umas 640 px para as colunas.</summary>
    private const double StackBelow = 1100;

    public EvalView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EvalViewModel vm)
            {
                vm.TerminalCommandRequested += async (_, _) =>
                {
                    if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    {
                        try
                        {
                            await clipboard.SetTextAsync(vm.TerminalCommand);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            // A área de transferência pode estar travada por outro programa; clicar de novo costuma funcionar.
                        }
                    }
                };
            }
        };
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

        body.ColumnDefinitions = ColumnDefinitions.Parse(narrow ? "*" : "*,380");
        body.RowDefinitions = RowDefinitions.Parse(narrow ? "Auto,Auto" : "Auto");
        Grid.SetRow(side, wanted);
        Grid.SetColumn(side, narrow ? 0 : 1);
    }
}
