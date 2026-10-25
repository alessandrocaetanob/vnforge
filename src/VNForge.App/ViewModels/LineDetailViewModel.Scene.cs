using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

public sealed partial class LineDetailViewModel
{
    /// <summary>"Na cena": a fala anterior e a seguinte do script, sem marcadores, na ordem em que aparecem; vazio quando o Editor não as carregou.</summary>
    public IReadOnlyList<ContextLine> Scene { get; init; } = [];

    public bool HasScene => Scene.Count > 0;

    /// <summary>As outras linhas do projeto com o mesmo original; nulo quando o Editor não montou o índice.</summary>
    public ISameSourceLines? SameSource { get; init; }

    public int SameSourceCount => SameSource?.Count ?? 0;

    public bool HasSameSource => SameSourceCount > 0;

    public string SameSourceText => SameSourceCount == 1 ? "Esta fala aparece em mais 1 linha." : $"Esta fala aparece em mais {SameSourceCount} linhas.";

    /// <summary>O que o "Usar em todos" fez, em uma frase; nulo antes de usar.</summary>
    [ObservableProperty]
    public partial string? SameSourceResult { get; private set; }

    /// <summary>Grava a tradução do campo em todas as outras linhas com o mesmo original. A linha aberta é gravada antes.</summary>
    [RelayCommand(CanExecute = nameof(HasSameSource))]
    private void UseForAll()
    {
        SameSourceResult = null;
        if (string.IsNullOrWhiteSpace(Translation))
        {
            ErrorMessage = "Escreva a tradução desta linha antes de usá-la nas outras.";
            return;
        }

        var applied = 0;
        if (Flush() && TrySave(() => applied = SameSource!.Apply(Translation)))
        {
            SameSourceResult = applied switch
            {
                0 => "As outras linhas já tinham esta tradução.",
                1 => "Aplicada a 1 linha.",
                _ => $"Aplicada a {applied} linhas.",
            };
        }
    }
}
