using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um item da lista de scripts do Editor: a chave, o percentual concluído e a barra de distribuição.</summary>
public sealed class ScriptItemViewModel(ScriptProgress progress)
{
    public ScriptProgress Progress { get; } = progress;

    public string Key => Progress.Key;

    public int CompletedPercent => Progress.CompletedPercent;

    public string PercentLabel => $"{CompletedPercent}%";

    public bool IsComplete => CompletedPercent == 100;

    public StatusDistribution Distribution { get; } = StatusDistribution.Of(progress.Counts);

    /// <summary>O percentual de IA aparece só aqui; o de "concluído" conta Revisadas e Aprovadas.</summary>
    public string Tooltip => $"IA {Progress.MachinePercent}%";

    public string LinesLabel => ProjectLabels.Number(Progress.Counts.Total);

    /// <summary>Descrição da barra de distribuição para leitor de tela.</summary>
    public string DistributionDescription =>
        $"Aprovadas {Distribution.Approved}%, revisadas {Distribution.Reviewed}%, traduzidas {Distribution.Translated}%, IA {Distribution.Machine}%, pendentes {Distribution.Pending}%";
}
