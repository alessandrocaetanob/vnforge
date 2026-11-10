using Avalonia.Controls;
using Avalonia.Input;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class GameSheetView : UserControl
{
    public GameSheetView() => InitializeComponent();

    /// <summary>Na revisão, Esc volta para a ficha e Page Down e Page Up passam de um bloco ao outro, sem tirar o foco do campo de texto.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is GameSheetViewModel { IsReviewing: true } sheet && !e.Handled)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    sheet.ExitReviewCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.PageDown:
                    sheet.Review.NextCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.PageUp:
                    sheet.Review.PreviousCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        base.OnKeyDown(e);
    }
}
