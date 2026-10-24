using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VNForge.App.Services;

/// <summary>Seletor de pasta nativo, ligado à janela dona.</summary>
public sealed class AvaloniaFolderPicker(TopLevel owner) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
