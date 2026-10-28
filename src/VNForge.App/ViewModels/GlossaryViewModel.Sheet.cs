using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VNForge.App.ViewModels;

/// <summary>A ficha do personagem (#469) na página Glossário.</summary>
public sealed partial class GlossaryViewModel
{
    /// <summary>A ficha aberta, ou nulo quando nenhuma está.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheet))]
    public partial CharacterSheetViewModel? Sheet { get; private set; }

    public bool HasSheet => Sheet is not null;

    partial void OnSheetChanging(CharacterSheetViewModel? value) => Sheet?.Dispose();

    [RelayCommand]
    private void OpenSheet(CharacterRow? row)
    {
        if (row is null || row.IsBlank)
        {
            return;
        }

        Sheet = new CharacterSheetViewModel(row, name => scan.Sheets.GetValueOrDefault(name), () => usageLoaded, r => ReviewCharacterCommand.Execute(r), CloseSheet);
    }

    [RelayCommand]
    private void CloseSheet() => Sheet = null;
}
