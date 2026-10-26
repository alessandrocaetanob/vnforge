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

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (DataContext is StartViewModel start && paths.Count > 0)
        {
            await start.DropFolderAsync(paths[0], paths.Count);
        }
    }
}
