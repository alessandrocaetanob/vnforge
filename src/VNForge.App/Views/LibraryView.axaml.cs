using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>A tela inicial: a Biblioteca. Só liga a área de soltar pasta; o resto é do modelo.</summary>
public sealed partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        DropArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropArea.AddHandler(DragDrop.DropEvent, OnDrop);
        LayoutUpdated += (_, _) => FitSortBar();
    }

    /// <summary>A busca encolhe até 72 para "Ordenar por" e a aparência ficarem na linha dos chips; se nem assim cabem, descem para uma linha própria.</summary>
    private const double SearchMinWidth = 72;

    private const double SortMargin = 8;

    private void FitSortBar()
    {
        var width = FilterRow.Bounds.Width;
        var items = SortBar.Children.Where(c => c.IsVisible).ToList();
        if (width <= 0 || items.Count == 0)
        {
            return;
        }

        var sortWidth = items.Sum(c => c.DesiredSize.Width) + (items.Count - 1) * SortBar.ItemSpacing;
        SortBar.Classes.Set("below", SearchMinWidth + FilterChips.DesiredSize.Width + SortMargin + sortWidth > width);
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().FirstOrDefault();
        if (DataContext is LibraryViewModel library && path is not null)
        {
            await library.DropFolderAsync(path);
        }
    }
}
