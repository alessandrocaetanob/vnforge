using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using VNForge.App.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>Só monta o original com os marcadores em selos (um <c>TextBlock</c> não faz isso por binding) e leva o foco à tela, para as teclas valerem; a lógica fica no <see cref="ReviewModeViewModel"/>.</summary>
public sealed partial class ReviewModeView : UserControl
{
    private ReviewModeViewModel? current;

    public ReviewModeView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (current is not null)
        {
            current.PropertyChanged -= OnViewModelPropertyChanged;
        }

        current = DataContext as ReviewModeViewModel;
        if (current is null)
        {
            return;
        }

        current.PropertyChanged += OnViewModelPropertyChanged;
        FillSource();
        FocusScreen();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusScreen();
    }

    private void FocusScreen() => Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewModeViewModel.Detail))
        {
            FillSource();
        }
    }

    private void FillSource()
    {
        SourceText.Inlines?.Clear();
        if (current?.Detail is not { } detail)
        {
            return;
        }

        foreach (var segment in detail.SourceSegments)
        {
            if (!segment.IsMarker)
            {
                SourceText.Inlines?.Add(new Run(segment.Text));
                continue;
            }

            var kind = segment.Text.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control;
            SourceText.Inlines?.Add(new InlineUIContainer(MarkerChipGenerator.CreateChip(segment.Text, kind)) { BaselineAlignment = BaselineAlignment.Center });
        }
    }
}
