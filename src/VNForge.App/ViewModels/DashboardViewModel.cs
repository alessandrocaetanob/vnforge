using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Uma etapa do pipeline no Painel. Traduzir leva ao Editor, Conferir à Conferência, Build e Injetar à tela Build.</summary>
public sealed class PipelineStageViewModel(int number, PipelineStage stage)
{
    public PipelineStageKey Key { get; } = stage.Key;

    public int Number { get; } = number;

    public string Label { get; } = stage.Key switch
    {
        PipelineStageKey.Extract => "Extrair",
        PipelineStageKey.Translate => "Traduzir",
        PipelineStageKey.Review => "Conferir",
        PipelineStageKey.Build => "Build",
        _ => "Injetar",
    };

    public bool IsComplete { get; } = stage.IsComplete;

    public int Pending { get; } = stage.Pending;

    public bool HasPending => !IsComplete;

    /// <summary>Pendências em linhas só para as etapas que contam linhas; as de tudo ou nada dizem "Pendente".</summary>
    public string StatusText => IsComplete ? "Concluída" : PendingText();

    /// <summary>Traduzir e Conferir abrem sua tela sempre; Build e Injetar só enquanto pendentes; Extrair não tem tela.</summary>
    public bool IsActionable => Key switch
    {
        PipelineStageKey.Translate or PipelineStageKey.Review => true,
        PipelineStageKey.Build or PipelineStageKey.Inject => !IsComplete,
        _ => false,
    };

    private string PendingText()
    {
        if (Pending == 0 || Key is not (PipelineStageKey.Translate or PipelineStageKey.Review))
        {
            return "Pendente";
        }

        return Pending == 1 ? "1 pendente" : $"{ProjectLabels.Number(Pending)} pendentes";
    }
}

/// <summary>Uma linha da tabela "Por arquivo" ou "Por personagem".</summary>
public sealed class ProgressRowViewModel(string name, StatusCounts counts, bool isMono)
{
    public string Name { get; } = name;

    public bool IsMono { get; } = isMono;

    public string LinesLabel { get; } = counts.Total == 1 ? "1 linha" : $"{ProjectLabels.Number(counts.Total)} linhas";

    public string PercentLabel { get; } = DashboardViewModel.Percent(counts);

    public bool IsComplete { get; } = counts.Percent >= 100;

    public StatusDistribution Distribution { get; } = StatusDistribution.Of(counts);

    public string DistributionDescription => DashboardViewModel.Describe(Distribution);
}

/// <summary>
/// Página Painel: etapas do pipeline com pendências, progresso por status, arquivo e personagem, e o custo de IA acumulado.
/// Os números vêm de <see cref="ProjectDashboard"/>, a mesma contagem do <c>vnf status</c>. Só lê; a página recarrega ao ser aberta.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    // Formato pt-BR fixo, como em ProjectLabels: não depende da cultura da máquina.
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private readonly Func<string, ProjectDashboard> loader;
    private int loadVersion;

    /// <param name="loader">Lê o painel de uma pasta de projeto; o padrão lê o disco.</param>
    public DashboardViewModel(Func<string, ProjectDashboard>? loader = null)
    {
        this.loader = loader ?? (root => ProjectDashboard.Read(ProjectFolder.Open(root)));
    }

    /// <summary>Pedido para ir à tela da etapa (a janela principal decide qual).</summary>
    public event EventHandler<PipelineStageKey>? StageRequested;

    public ObservableCollection<PipelineStageViewModel> Stages { get; } = [];

    /// <summary>Uma entrada por status, com percentual da barra e a contagem de linhas ("Aprovadas 21% · 10.123").</summary>
    public ObservableCollection<string> Legend { get; } = [];

    public ObservableCollection<ProgressRowViewModel> Files { get; } = [];

    public ObservableCollection<ProgressRowViewModel> Characters { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string LinesLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial StatusDistribution Distribution { get; private set; } = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));

    [ObservableProperty]
    public partial string DistributionDescription { get; private set; } = string.Empty;

    /// <summary>"62,5% com tradução", o mesmo número da primeira linha do <c>vnf status</c>.</summary>
    [ObservableProperty]
    public partial string ProgressLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CostLabel { get; private set; } = string.Empty;

    public bool HasProject => Project is not null;

    /// <summary>Lê os números de novo. Uma leitura mais nova descarta o resultado de uma mais antiga que ainda esteja em curso.</summary>
    public async Task ReloadAsync()
    {
        var project = Project;
        var version = ++loadVersion;
        if (project is null)
        {
            Clear();
            return;
        }

        ProjectDashboard? dashboard = null;
        string? error = null;
        try
        {
            dashboard = await Task.Run(() => loader(project.Root));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = $"Não foi possível ler o projeto: {ex.Message}";
        }

        if (version != loadVersion)
        {
            return;
        }

        if (dashboard is null)
        {
            Clear();
            ErrorMessage = error;
            return;
        }

        Apply(dashboard);
    }

    [RelayCommand]
    private void OpenStage(PipelineStageViewModel? stage)
    {
        if (stage is { IsActionable: true })
        {
            StageRequested?.Invoke(this, stage.Key);
        }
    }

    /// <summary>Percentual com tradução como o <c>vnf status</c> o escreve: uma casa no máximo, vírgula decimal.</summary>
    public static string Percent(StatusCounts counts) => string.Create(PtBr, $"{counts.Percent:0.#}%");

    public static string Describe(StatusDistribution d) =>
        $"Aprovadas {d.Approved}%, revisadas {d.Reviewed}%, traduzidas {d.Translated}%, IA {d.Machine}%, pendentes {d.Pending}%";

    /// <summary><c>US$ 2,10</c>; abaixo de um centavo mostra quatro casas para o gasto não parecer zero.</summary>
    public static string Dollars(decimal value) =>
        value is > 0m and < 0.01m
            ? string.Create(PtBr, $"US$ {value:0.0000}")
            : string.Create(PtBr, $"US$ {value:N2}");

    /// <summary><c>2,1 M tokens</c>, <c>850 mil tokens</c> ou <c>420 tokens</c>.</summary>
    public static string Tokens(long value) => value switch
    {
        >= 1_000_000 => string.Create(PtBr, $"{value / 1_000_000.0:0.#} M tokens"),
        >= 1_000 => string.Create(PtBr, $"{value / 1_000.0:0.#} mil tokens"),
        _ => string.Create(PtBr, $"{value:N0} tokens"),
    };

    /// <summary>
    /// <c>US$ 2,10 · 2,1 M tokens em 3 execuções</c>. Execução de modelo sem preço conhecido não entra no dólar: o texto avisa que o custo é parcial
    /// (ou desconhecido, se nenhuma tinha preço) em vez de mostrar US$ 0,00.
    /// </summary>
    public static string DescribeCost(AiUsageTotals usage)
    {
        if (usage.Runs == 0)
        {
            return "Nenhuma chamada de IA registrada neste projeto.";
        }

        var runs = usage.Runs == 1 ? "1 execução" : $"{ProjectLabels.Number(usage.Runs)} execuções";
        var tokens = $"{Tokens(usage.TotalTokens)} em {runs}";
        if (usage.UnpricedRuns == 0)
        {
            return $"{Dollars(usage.Dollars)} · {tokens}";
        }

        if (usage.UnpricedRuns >= usage.Runs)
        {
            return $"Custo desconhecido (modelo sem preço na tabela) · {tokens}";
        }

        var unpriced = usage.UnpricedRuns == 1 ? "1 execução sem preço" : $"{ProjectLabels.Number(usage.UnpricedRuns)} execuções sem preço";
        return $"{Dollars(usage.Dollars)} (parcial: {unpriced}) · {tokens}";
    }

    /// <summary>Ao trocar de projeto os números do anterior saem na hora, e uma leitura ainda em curso deixa de valer.</summary>
    partial void OnProjectChanged(ProjectSummary? value)
    {
        loadVersion++;
        Clear();
    }

    private void Clear()
    {
        Stages.Clear();
        Files.Clear();
        Characters.Clear();
        ErrorMessage = null;
        LinesLabel = string.Empty;
        Distribution = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));
        DistributionDescription = string.Empty;
        Legend.Clear();
        ProgressLabel = CostLabel = string.Empty;
    }

    private void Apply(ProjectDashboard dashboard)
    {
        var total = dashboard.Status.Total;
        var distribution = StatusDistribution.Of(total);

        Stages.Clear();
        foreach (var (stage, index) in dashboard.Stages.Select((s, i) => (s, i)))
        {
            Stages.Add(new PipelineStageViewModel(index + 1, stage));
        }

        Files.Clear();
        foreach (var file in dashboard.Status.Files)
        {
            Files.Add(new ProgressRowViewModel(file.Key, file.Counts, isMono: true));
        }

        Characters.Clear();
        foreach (var character in dashboard.Status.Characters)
        {
            Characters.Add(new ProgressRowViewModel(character.Speaker ?? "(sem falante)", character.Counts, isMono: false));
        }

        ErrorMessage = null;
        LinesLabel = ProjectLabels.Number(total.Total);
        Distribution = distribution;
        DistributionDescription = Describe(distribution);
        ProgressLabel = $"{Percent(total)} com tradução";
        Legend.Clear();
        Legend.Add($"Aprovadas {distribution.Approved}% · {ProjectLabels.Number(total.Approved)}");
        Legend.Add($"Revisadas {distribution.Reviewed}% · {ProjectLabels.Number(total.Reviewed)}");
        Legend.Add($"Traduzidas {distribution.Translated}% · {ProjectLabels.Number(total.Translated)}");
        Legend.Add($"IA {distribution.Machine}% · {ProjectLabels.Number(total.Machine)}");
        Legend.Add($"Pendentes {distribution.Pending}% · {ProjectLabels.Number(total.Untranslated)}");
        CostLabel = DescribeCost(dashboard.AiUsage);
    }
}
