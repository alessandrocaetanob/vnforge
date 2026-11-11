using Avalonia.Controls;
using Avalonia.Threading;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class GamePlayView : UserControl
{
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public GamePlayView()
    {
        InitializeComponent();
        tick.Tick += (_, _) => (DataContext as GamePlayViewModel)?.Tick();
    }

    // O relógio da partida só anda com a aba à vista.
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        tick.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        tick.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
