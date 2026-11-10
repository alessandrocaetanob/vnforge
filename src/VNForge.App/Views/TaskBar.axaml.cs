using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Views;

public sealed partial class TaskBar : UserControl
{
    /// <summary>Onde a borda esquerda da lista de tarefas fica, contada da borda esquerda da janela: o trilho (80) mais o respiro da página (16), como no quadro.</summary>
    public const double FlyoutLeft = 96;

    public TaskBar()
    {
        InitializeComponent();
        if (TasksButton.Flyout is Flyout flyout)
        {
            flyout.Opening += (_, _) => AlignFlyout(flyout);
        }
    }

    /// <summary>
    /// O quadro põe a lista sobre o Editor, no canto inferior esquerdo, e não embaixo do botão. A lista abre acima do botão (<c>TopEdgeAlignedLeft</c>), então basta
    /// um deslocamento horizontal que leve a borda esquerda dela até <see cref="FlyoutLeft"/>; o vertical (8 px acima do botão) está no XAML.
    /// </summary>
    private void AlignFlyout(Flyout flyout)
    {
        if (TopLevel.GetTopLevel(TasksButton) is { } top && TasksButton.TranslatePoint(default, top) is { } origin)
        {
            flyout.HorizontalOffset = FlyoutLeft - origin.X;
        }
    }
}
