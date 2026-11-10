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
