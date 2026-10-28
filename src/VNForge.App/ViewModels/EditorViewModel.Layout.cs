using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>
/// O arranjo do Editor que a pessoa escolhe e o app lembra: a coluna das cenas, aberta ou recolhida numa faixa estreita com o nome da cena aberta (#393),
/// e a densidade da grade, em colunas ou como roteiro (#460).
/// O estado vive em <see cref="EditorLayoutMemory"/>; sem ela vale só até o app fechar.
/// </summary>
public sealed partial class EditorViewModel
{
    private EditorLayoutMemory? layoutMemory;

    /// <summary>A coluna das cenas está recolhida. Abaixo de 1400 px a lista já mora no botão Cenas do cabeçalho e este estado só vale quando a janela volta a ser larga.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScenesToggleTip))]
    public partial bool IsScenesCollapsed { get; set; }

    /// <summary>A dica do botão que recolhe e abre a coluna, com o atalho.</summary>
    public string ScenesToggleTip => IsScenesCollapsed ? "Abrir a lista de cenas (Ctrl+B)" : "Recolher a lista de cenas (Ctrl+B)";

    /// <summary>A grade mostra cada linha como roteiro: o nome de quem fala como rótulo, o original em cima e a tradução embaixo, até duas linhas cada. Falso são as colunas de sempre.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DensityTip))]
    public partial bool IsScriptDensity { get; set; }

    /// <summary>A dica do botão que troca a densidade da grade.</summary>
    public string DensityTip => IsScriptDensity ? "Voltar às linhas em colunas" : "Mostrar as linhas como roteiro: o original em cima e a tradução embaixo";

    /// <summary>Lê o estado lembrado; chamado pelo construtor.</summary>
    private void RestoreLayout(EditorLayoutMemory? memory)
    {
        layoutMemory = memory;
        IsScenesCollapsed = memory?.ScenesCollapsed ?? false;
        IsScriptDensity = memory?.ScriptDensity ?? false;
    }

    [RelayCommand]
    private void ToggleScenes() => IsScenesCollapsed = !IsScenesCollapsed;

    [RelayCommand]
    private void ToggleDensity() => IsScriptDensity = !IsScriptDensity;

    partial void OnIsScenesCollapsedChanged(bool value)
    {
        if (layoutMemory is not null)
        {
            layoutMemory.ScenesCollapsed = value;
        }
    }

    partial void OnIsScriptDensityChanged(bool value)
    {
        if (layoutMemory is not null)
        {
            layoutMemory.ScriptDensity = value;
        }
    }
}
