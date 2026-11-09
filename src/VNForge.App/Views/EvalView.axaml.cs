using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class EvalView : UserControl
{
    /// <summary>Abaixo desta largura o gráfico e a leitura passam para baixo da tabela, que precisa de umas 640 px para as colunas.</summary>
    private const double StackBelow = 1100;

    private bool narrow;

    public EvalView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => FillToBottom();
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

    /// <summary>Esc sai da rodada às cegas e volta ao relatório: o quadro não desenha o link "Voltar ao relatório".</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e is { Key: Key.Escape, Handled: false } && DataContext is EvalViewModel { IsBlind: true } vm && vm.BlindBackCommand.CanExecute(null))
        {
            vm.BlindBackCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Stack(bool narrowNow)
    {
        narrow = narrowNow;
        if (this.FindControl<Grid>("Body") is not { } body || this.FindControl<Border>("Side") is not { } side)
        {
            return;
        }

        var wanted = narrowNow ? 1 : 0;
        if (Grid.GetRow(side) == wanted)
        {
            return;
        }

        body.ColumnDefinitions = ColumnDefinitions.Parse(narrowNow ? "*" : "*,380");
        body.RowDefinitions = RowDefinitions.Parse(narrowNow ? "Auto,Auto" : "*");
        Grid.SetRow(side, wanted);
        Grid.SetColumn(side, narrowNow ? 0 : 1);
    }

    /// <summary>
    /// No quadro a tabela e o cartão da direita vão até o pé da janela. O Painel rola dentro de um <see cref="ScrollViewer"/>, que dá altura sem limite ao conteúdo; aqui a
    /// altura mínima do relatório vira o que sobra da janela abaixo dele, e a tela só rola quando as linhas passam disso.
    /// </summary>
    private void FillToBottom()
    {
        if (this.FindControl<Grid>("Body") is not { IsVisible: true } body || this.FindAncestorOfType<ScrollViewer>() is not { Content: Layoutable content } scroller)
        {
            return;
        }

        var wanted = 0d;
        if (!narrow && body.TranslatePoint(default, content) is { } origin)
        {
            wanted = Math.Max(0, scroller.Viewport.Height - content.Bounds.Y - origin.Y - content.Margin.Bottom);
        }

        if (Math.Abs(body.MinHeight - wanted) > 0.5)
        {
            body.MinHeight = wanted;
        }
    }
}
