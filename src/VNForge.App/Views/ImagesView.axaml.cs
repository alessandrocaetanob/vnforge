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

    // "Apagar" e "Texto" levam a pessoa ao campo da propriedade: o de como apagar a área e o do texto da tradução.
    private void OnEraseToolClick(object? sender, RoutedEventArgs e) => EraseModes.Focus();

    private void OnTextToolClick(object? sender, RoutedEventArgs e) => ImageText.Focus();

    // A moldura e as alças são desenhadas em pixels da imagem, dentro do Viewbox; o inverso da escala dele mantém o traço do quadro (2px) e as alças (10px) na tela.
    private void OnOriginalSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Model is { PixelWidth: > 0 } model && OriginalBox.Bounds.Width > 0)
        {
            model.OverlayScale = OriginalBox.Bounds.Width / model.PixelWidth;
        }
    }

    // As coordenadas do ponteiro já vêm em pixels da imagem: o Viewbox escala a superfície, que tem o tamanho real da imagem.
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(OriginalSurface).Properties.IsLeftButtonPressed || Model is not { } model)
        {
            return;
        }

        var at = e.GetPosition(OriginalSurface);
        // Pegar uma alça redimensiona a área: o canto oposto fica parado. Em qualquer outro ponto o arrastar começa uma área nova.
        dragStart = model.TryResizeAnchor(at.X, at.Y, out var anchor) ? new Point(anchor.X, anchor.Y) : at;
        model.UseToolCommand.Execute(ImageTool.SelectArea);
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
