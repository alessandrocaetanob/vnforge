using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>
/// O caminho do Locale Emulator, que o Jogar usa nos jogos que pedem localidade japonesa. Fica no <c>library.json</c>, o mesmo que <c>vnf library emulator</c> grava,
/// e só é aceito quando leva a um <c>LEProc.exe</c> que existe.
/// </summary>
public sealed partial class LocaleEmulatorSettingsViewModel : ObservableObject
{
    private readonly LibraryFile file;
    private readonly IFolderPicker picker;

    public LocaleEmulatorSettingsViewModel(LibraryFile file, IFolderPicker picker)
    {
        this.file = file;
        this.picker = picker;
        Load();
    }

    /// <summary>A pasta do Locale Emulator ou o <c>LEProc.exe</c>.</summary>
    [ObservableProperty]
    public partial string PathText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>Relê o <c>library.json</c>: o caminho pode ter mudado pela CLI.</summary>
    public void Load()
    {
        try
        {
            var path = file.Load().LocaleEmulatorPath;
            PathText = path ?? string.Empty;
            Report(path is null ? "Não configurado. Os jogos que pedem localidade japonesa só abrem depois que você informar o caminho." : $"Locale Emulator encontrado em {path}.");
        }
        catch (LibraryException e)
        {
            Report(e.Message);
        }
    }

    /// <summary>Confere o caminho e grava; em branco, esquece o caminho.</summary>
    [RelayCommand]
    private void Save()
    {
        try
        {
            var resolved = string.IsNullOrWhiteSpace(PathText) ? null : GameLaunchPlanner.ResolveLocaleEmulator(PathText);
            file.Update(library => library.LocaleEmulatorPath = resolved);
            PathText = resolved ?? string.Empty;
            Report(resolved is null ? "Caminho esquecido." : $"Locale Emulator encontrado em {resolved}.");
        }
        catch (LibraryException e)
        {
            Report(e.Message);
        }
    }

    [RelayCommand]
    private async Task ChooseAsync()
    {
        if (await picker.PickFolderAsync("Pasta do Locale Emulator") is { } folder)
        {
            PathText = folder;
            Save();
        }
    }

    private void Report(string text) => Status = text;
}
