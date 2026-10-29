using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>A ficha do personagem (#469) na página Glossário.</summary>
public sealed partial class GlossaryViewModel
{
    /// <summary>A ficha aberta, ou nulo quando nenhuma está.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheet))]
    public partial CharacterSheetViewModel? Sheet { get; private set; }

    public bool HasSheet => Sheet is not null;

    /// <summary>A leitura do texto da última ficha aberta; os testes a esperam.</summary>
    public Task SheetLoading { get; private set; } = Task.CompletedTask;

    partial void OnSheetChanging(CharacterSheetViewModel? value) => Sheet?.Dispose();

    [RelayCommand]
    private void OpenSheet(CharacterRow? row)
    {
        if (row is null || row.IsBlank)
        {
            return;
        }

        var sheet = new CharacterSheetViewModel(row, r => ReviewCharacterCommand.Execute(r), CloseSheet);
        Sheet = sheet;
        SheetLoading = LoadSheetAsync(sheet);
    }

    [RelayCommand]
    private void CloseSheet() => Sheet = null;

    /// <summary>
    /// Lê o texto do projeto a cada abertura, fora da thread da tela: o status das linhas muda no Editor e na tradução em lote sem o Glossário saber,
    /// então a leitura feita ao abrir a página ficaria velha. Uma ficha trocada, fechada ou de outro projeto descarta a resposta.
    /// </summary>
    private async Task LoadSheetAsync(CharacterSheetViewModel sheet)
    {
        var root = Project?.Root;
        IReadOnlyDictionary<string, SpeakerSheet>? sheets = null;
        if (root is not null)
        {
            try
            {
                sheets = await Task.Run(
                    () => SpeakerSamples.Sheets(ProjectFolder.Open(root).LoadAllText().Values.SelectMany(units => units)), CancellationToken.None);
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                // Sem o texto a ficha avisa que não leu, em vez de dizer que o personagem não fala.
            }
        }

        if (!ReferenceEquals(Sheet, sheet))
        {
            return;
        }

        if (sheets is null)
        {
            sheet.Fail();
        }
        else
        {
            sheet.Apply(sheets);
        }
    }
}
