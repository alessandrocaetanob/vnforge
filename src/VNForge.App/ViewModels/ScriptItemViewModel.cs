using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um item da lista de scripts do Editor: a chave, o nome do capítulo, o percentual concluído e a barra de distribuição.</summary>
/// <param name="progress">O progresso do script.</param>
/// <param name="label">O nome legível do script; nulo mostra só a chave.</param>
public sealed partial class ScriptItemViewModel(ScriptProgress progress, string? label = null) : ObservableObject
{
    /// <summary>O retrato do progresso; <see cref="Update"/> o troca quando as linhas do script mudam sem o projeto ser relido.</summary>
    public ScriptProgress Progress { get; private set; } = progress;

    public string Key => Progress.Key;

    /// <summary>O nome do capítulo ("Prólogo"); nulo enquanto o projeto não o guarda.</summary>
    public string? Label { get; } = string.IsNullOrWhiteSpace(label) ? null : label;

    /// <summary>O nome do capítulo, ou a chave quando ele não existe.</summary>
    public string Display => Label ?? Key;

    public int CompletedPercent => Progress.CompletedPercent;

    public string PercentLabel => $"{CompletedPercent}%";

    public bool IsComplete => CompletedPercent == 100;

    /// <summary>Há uma tarefa da IA traduzindo este script: o item mostra o ícone girando.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>O item passa pelo filtro da lista de scripts; a lista esconde os que não passam sem tirá-los dela.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    public StatusDistribution Distribution { get; private set; } = StatusDistribution.Of(progress.Counts);

    /// <summary>O percentual de IA aparece só aqui; o de "concluído" conta Revisadas e Aprovadas.</summary>
    public string Tooltip => $"IA {Progress.MachinePercent}%";

    public string LinesLabel => ProjectLabels.Number(Progress.Counts.Total);

    /// <summary>Descrição da barra de distribuição para leitor de tela.</summary>
    public string DistributionDescription =>
        $"Aprovadas {Distribution.Approved}%, revisadas {Distribution.Reviewed}%, traduzidas {Distribution.Translated}%, IA {Distribution.Machine}%, pendentes {Distribution.Pending}%";

    /// <summary>
    /// Troca as contagens do script (uma linha gravada, aprovada ou aplicada por "Usar em todos") e avisa a lista e o rodapé, que não esperam
    /// a próxima leitura do projeto. Contagens iguais às de agora não notificam.
    /// </summary>
    public void Update(StatusCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (Progress.Counts == counts)
        {
            return;
        }

        Progress = Progress with { Counts = counts };
        Distribution = StatusDistribution.Of(counts);
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(CompletedPercent));
        OnPropertyChanged(nameof(PercentLabel));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(Distribution));
        OnPropertyChanged(nameof(DistributionDescription));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(LinesLabel));
    }

    /// <summary>Casa o texto do filtro da lista com a chave ou o nome, sem diferenciar maiúsculas.</summary>
    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || Key.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)
        || (Label?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);
}
