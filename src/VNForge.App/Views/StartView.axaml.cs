using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
        DropArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropArea.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is StartViewModel start && e.DataTransfer.TryGetFile() is { } item && item.TryGetLocalPath() is { } path)
        {
            start.DropFolder(path);
        }
    }
}
