using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class ImagesView : UserControl
{
    private Point? dragStart;

    public ImagesView()
    {
        InitializeComponent();
        OriginalSurface.PointerPressed += OnPressed;
        OriginalSurface.PointerMoved += OnMoved;
        OriginalSurface.PointerReleased += (_, e) =>
        {
            dragStart = null;
            e.Pointer.Capture(null);
        };
    }

    private ImagesViewModel? Model => DataContext as ImagesViewModel;

    private void OnChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ImageFilterChipViewModel chip })
        {
            Model?.SetFilterCommand.Execute(chip.Filter);
        }
    }

    // As coordenadas do ponteiro já vêm em pixels da imagem: o Viewbox escala a superfície, que tem o tamanho real da imagem.
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(OriginalSurface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        dragStart = e.GetPosition(OriginalSurface);
        e.Pointer.Capture(OriginalSurface);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (dragStart is not { } start || Model is not { } model)
        {
            return;
        }

        var now = e.GetPosition(OriginalSurface);
        model.SetRegion((int)Math.Min(start.X, now.X), (int)Math.Min(start.Y, now.Y), (int)Math.Abs(now.X - start.X), (int)Math.Abs(now.Y - start.Y));
    }
}
