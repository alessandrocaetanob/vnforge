using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Uma etapa do pipeline no Painel. Traduzir leva ao Editor, Conferir à Conferência, Build e Injetar à tela Build; Extrair não tem tela.</summary>
public sealed class PipelineStageViewModel(int number, PipelineStage stage, string subtitle = "")
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

    /// <summary>O resumo sob o nome da etapa no <see cref="PipelineStepper"/> ("62% · 18.320 pendentes").</summary>
    public string Subtitle { get; } = subtitle;

    /// <summary>Pendências em linhas só para as etapas que contam linhas; as de tudo ou nada dizem "Pendente".</summary>
    public string StatusText => IsComplete ? "Concluída" : PendingText();

    /// <summary>Toda etapa leva à sua tela, concluída ou não; só Extrair não tem tela.</summary>
    public bool IsActionable => Key != PipelineStageKey.Extract;

    private string PendingText()
    {
        if (Pending == 0 || Key is not (PipelineStageKey.Translate or PipelineStageKey.Review))
        {
            return "Pendente";
        }

        return Pending == 1 ? "1 pendente" : $"{ProjectLabels.Number(Pending)} pendentes";
    }
}

/// <summary>Uma linha da tabela "Por script": chave em mono, nome legível, linhas, barra e a porcentagem concluída (aprovadas e revisadas).</summary>
public sealed class ScriptRowViewModel(string key, string? name, StatusCounts counts)
{
    public string Key { get; } = key;

    /// <summary>O nome do usuário ou o que a engine leu do jogo; vazio quando o script não tem nome.</summary>
    public string Name { get; } = name ?? string.Empty;

    public string LinesLabel { get; } = ProjectLabels.Number(counts.Total);

    public string PercentLabel { get; } = DashboardViewModel.CompletedPercentLabel(counts);

    public StatusDistribution Distribution { get; } = StatusDistribution.Of(counts);

    public string DistributionDescription => DashboardViewModel.Describe(Distribution);
}

/// <summary>
/// Uma linha da tabela "Por personagem": o nome original e a tradução ("美咲 → Misaki"), linhas, barra e a porcentagem concluída.
/// Sem tradução cadastrada mostra só o nome; linhas sem falante são a "Narração".
/// </summary>
public sealed class CharacterRowViewModel(string? speaker, string? translation, StatusCounts counts)
{
    public const string NarrationLabel = "Narração";

    /// <summary>O nome no idioma original, quando há uma tradução para mostrar ao lado dele.</summary>
    public string? Source { get; } = speaker is not null && translation is not null ? speaker : null;

    public bool HasSource => Source is not null;

    public string Name { get; } = speaker is null ? NarrationLabel : translation ?? speaker;

    public bool IsNarration { get; } = speaker is null;

    public string LinesLabel { get; } = ProjectLabels.Number(counts.Total);

    public string PercentLabel { get; } = DashboardViewModel.CompletedPercentLabel(counts);

    public StatusDistribution Distribution { get; } = StatusDistribution.Of(counts);

    public string DistributionDescription => DashboardViewModel.Describe(Distribution);
}

/// <summary>Para onde um botão do Painel leva: a tela e, no Editor, o script a abrir.</summary>
public sealed record DashboardNavigation(NavKey Target, string? ScriptKey = null);

/// <summary>
/// Página Painel: cabeçalho com o projeto, as cinco etapas do pipeline, os cartões de Linhas, Uso da IA e Conferência e as tabelas por script e por
/// personagem. Os números vêm de <see cref="ProjectDashboard"/>, a mesma contagem do <c>vnf status</c> e do <c>vnf check</c>. Só lê; a página recarrega ao ser aberta.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    /// <summary>Quantas linhas cada tabela mostra; o resto fica no Editor e no Glossário.</summary>
    public const int TableRows = 8;

    // Formato pt-BR fixo, como em ProjectLabels: não depende da cultura da máquina.
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private readonly Func<string, ProjectDashboard> loader;
    private readonly Action<string> folderOpener;
    private int loadVersion;
    private string? continueKey;

    /// <param name="loader">Lê o painel de uma pasta de projeto; o padrão lê o disco.</param>
    /// <param name="folderOpener">Abre uma pasta no gerenciador de arquivos do sistema; o padrão usa o shell.</param>
    public DashboardViewModel(Func<string, ProjectDashboard>? loader = null, Action<string>? folderOpener = null)
    {
        this.loader = loader ?? (root => ProjectDashboard.Read(ProjectFolder.Open(root)));
        this.folderOpener = folderOpener ?? ShellOpen;
    }

    /// <summary>Pedido para ir à tela da etapa (a janela principal decide qual).</summary>
    public event EventHandler<PipelineStageKey>? StageRequested;

    /// <summary>Pedido para ir a uma tela (Editor, Conferência, Ajustes ou Glossário), de um botão ou link do Painel.</summary>
    public event EventHandler<DashboardNavigation>? NavigationRequested;

    public ObservableCollection<PipelineStageViewModel> Stages { get; } = [];

    /// <summary>As etapas como o <see cref="PipelineStepper"/> as recebe: número, nome, resumo, estado e o comando que abre a tela.</summary>
    public ObservableCollection<PipelineStep> Steps { get; } = [];

    /// <summary>Legenda do cartão de Uso da IA: gasto e restante estimado.</summary>
    public ObservableCollection<LegendEntry> AiLegend { get; } = [];

    /// <summary>Legenda do cartão de Conferência: erros e avisos.</summary>
    public ObservableCollection<LegendEntry> ConferenceLegend { get; } = [];

    public ObservableCollection<ScriptRowViewModel> Scripts { get; } = [];

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Breadcrumb))]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>"Continuar em yst00013": o primeiro script com linhas pendentes, ou o primeiro com linhas à espera de conferência; vazio sem script.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    public partial string ContinueLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LinesLabel { get; private set; } = string.Empty;

    /// <summary>"39% concluídas · 18.320 pendentes": concluída é aprovada ou revisada, como a coluna % das tabelas.</summary>
    [ObservableProperty]
    public partial string LinesDescription { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial StatusDistribution Distribution { get; private set; } = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));

    [ObservableProperty]
    public partial string DistributionDescription { get; private set; } = string.Empty;

    /// <summary>"62,5% com tradução", o mesmo número da primeira linha do <c>vnf status</c>.</summary>
    [ObservableProperty]
    public partial string ProgressLabel { get; private set; } = string.Empty;

    /// <summary>O gasto de IA em dólar ("US$ 26,40").</summary>
    [ObservableProperty]
    public partial string AiValue { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AiDescription { get; private set; } = string.Empty;

    /// <summary>O gasto sobre o gasto mais o restante estimado, em barra; vazia sem estimativa.</summary>
    [ObservableProperty]
    public partial StatusDistribution AiDistribution { get; private set; } = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));

    [ObservableProperty]
    public partial string AiBarDescription { get; private set; } = string.Empty;

    /// <summary>"39 problemas", "1 problema" ou "Nenhum problema".</summary>
    [ObservableProperty]
    public partial string ConferenceValue { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ConferenceDescription { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int ConferenceErrors { get; private set; }

    [ObservableProperty]
    public partial int ConferenceWarnings { get; private set; }

    [ObservableProperty]
    public partial string ConferenceBarDescription { get; private set; } = string.Empty;

    /// <summary>"8 de 312 scripts".</summary>
    [ObservableProperty]
    public partial string ScriptsFooter { get; private set; } = string.Empty;

    /// <summary>"8 de 23 personagens".</summary>
    [ObservableProperty]
    public partial string CharactersFooter { get; private set; } = string.Empty;

    public bool HasProject => Project is not null;

    public bool CanContinue => continueKey is not null;

    /// <summary>O nome do projeto, o título da página.</summary>
    public string Title => Project?.Name ?? string.Empty;

    /// <summary>A linha de cima do cabeçalho: a pasta do projeto e a engine ("D:\Traducoes\minha-vn · YU-RIS · YPF v500").</summary>
    public string Breadcrumb => Project is { } p ? $"{p.Root} · {ProjectLabels.Engine(p.EngineId, p.EngineVersion)}" : string.Empty;

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

        Apply(project, dashboard);
    }

    [RelayCommand]
    private void OpenStage(PipelineStageViewModel? stage)
    {
        if (stage is { IsActionable: true })
        {
            StageRequested?.Invoke(this, stage.Key);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Project is { } project)
        {
            folderOpener(project.Root);
        }
    }

    [RelayCommand]
    private void Continue()
    {
        if (continueKey is { } key)
        {
            NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Editor, key));
        }
    }

    [RelayCommand]
    private void OpenEditor() => NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Editor));

    [RelayCommand]
    private void OpenSettings() => NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Settings));

    [RelayCommand]
    private void OpenConference() => NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Conference));

    [RelayCommand]
    private void OpenGlossary() => NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Glossary));

    /// <summary>Aprovadas e revisadas sobre o total, em inteiro arredondado; só chega a 100 quando tudo está concluído (99 enquanto falta algo); 0 sem linhas.</summary>
    public static int CompletedPercent(StatusCounts counts)
    {
        if (counts.Total == 0)
        {
            return 0;
        }

        var completed = counts.Approved + counts.Reviewed;
        var percent = (int)Math.Round(100.0 * completed / counts.Total, MidpointRounding.AwayFromZero);
        return completed < counts.Total ? Math.Min(percent, 99) : 100;
    }

    public static string CompletedPercentLabel(StatusCounts counts) => $"{CompletedPercent(counts)}%";

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

    /// <summary><c>2,1 M</c>, <c>850 mil</c> ou <c>420</c>: o número de <see cref="Tokens"/> sem a palavra, para uma segunda quantidade na mesma frase.</summary>
    public static string TokensShort(long value) => Tokens(value).Replace(" tokens", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Quanto falta gastar para traduzir o que está pendente, pelo custo médio por linha já traduzida (gasto sobre linhas que saíram de pendente).
    /// Nulo sem como estimar: nada gasto, nenhuma linha traduzida ou execuções sem preço (o gasto seria parcial).
    /// </summary>
    public static decimal? EstimateRemaining(AiUsageTotals usage, StatusCounts counts)
    {
        if (usage.Dollars <= 0m || usage.UnpricedRuns > 0 || counts.Done == 0)
        {
            return null;
        }

        return Math.Round(usage.Dollars / counts.Done * counts.Untranslated, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Ao trocar de projeto os números do anterior saem na hora, e uma leitura ainda em curso deixa de valer.</summary>
    partial void OnProjectChanged(ProjectSummary? value)
    {
        loadVersion++;
        Clear();
    }

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{ProjectLabels.Number(count)} {many}";

    private static void ShellOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Debug.WriteLine(e);
        }
    }

    private void Clear()
    {
        Stages.Clear();
        Steps.Clear();
        Scripts.Clear();
        Characters.Clear();
        AiLegend.Clear();
        ConferenceLegend.Clear();
        ErrorMessage = null;
        continueKey = null;
        ContinueLabel = string.Empty;
        LinesLabel = LinesDescription = DistributionDescription = ProgressLabel = string.Empty;
        AiValue = AiDescription = AiBarDescription = string.Empty;
        ConferenceValue = ConferenceDescription = ConferenceBarDescription = string.Empty;
        ScriptsFooter = CharactersFooter = string.Empty;
        ConferenceErrors = ConferenceWarnings = 0;
        Distribution = AiDistribution = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));
        OnPropertyChanged(nameof(CanContinue));
    }

    private void Apply(ProjectSummary project, ProjectDashboard dashboard)
    {
        var total = dashboard.Status.Total;
        var distribution = StatusDistribution.Of(total);

        ApplyStages(dashboard);
        ApplyContinue(dashboard);

        ErrorMessage = null;
        LinesLabel = ProjectLabels.Number(total.Total);
        LinesDescription = $"{CompletedPercent(total)}% concluídas · {Plural(total.Untranslated, "pendente", "pendentes")}";
        Distribution = distribution;
        DistributionDescription = Describe(distribution);
        ProgressLabel = $"{Percent(total)} com tradução";

        ApplyAi(project, dashboard, total);
        ApplyConference(dashboard);
        ApplyTables(dashboard);
    }

    private void ApplyStages(ProjectDashboard dashboard)
    {
        var total = dashboard.Status.Total;
        var states = PipelineStepper.Resolve([.. dashboard.Stages.Select(s => s.IsComplete)]);
        Stages.Clear();
        Steps.Clear();
        foreach (var (stage, index) in dashboard.Stages.Select((s, i) => (s, i)))
        {
            var vm = new PipelineStageViewModel(index + 1, stage, Subtitle(stage, dashboard, total));
            Stages.Add(vm);
            Steps.Add(new PipelineStep(vm.Number, vm.Label, vm.Subtitle, states[index], OpenStageCommand, vm));
        }
    }

    private static string Subtitle(PipelineStage stage, ProjectDashboard dashboard, StatusCounts total) => stage.Key switch
    {
        PipelineStageKey.Extract => stage.IsComplete
            ? $"{Plural(total.Total, "linha", "linhas")} em {Plural(dashboard.Status.Files.Count, "script", "scripts")}"
            : "Ainda não extraído",
        PipelineStageKey.Translate => stage.IsComplete
            ? $"{Percent(total)} · nada pendente"
            : $"{Percent(total)} · {Plural(stage.Pending, "pendente", "pendentes")}",
        PipelineStageKey.Review => dashboard.Qa switch
        {
            null => "Conferência indisponível",
            { Total: 0 } => "Nenhum problema",
            var qa => $"{Plural(qa.Errors, "erro", "erros")} · {Plural(qa.Warnings, "aviso", "avisos")}",
        },
        PipelineStageKey.Build => dashboard.Build switch
        {
            BuildStatus.Current => "Build em dia",
            BuildStatus.Stale => "Os textos mudaram desde o último",
            _ => "Nenhum build ainda",
        },
        _ => InjectSubtitle(stage, dashboard),
    };

    private static string InjectSubtitle(PipelineStage stage, ProjectDashboard dashboard)
    {
        var backup = dashboard.LastBackupAt is { } at ? $"backup de {at.ToString("dd/MM", CultureInfo.InvariantCulture)}" : null;
        return stage.IsComplete
            ? backup is null ? "Injetado no jogo" : $"Injetado · {backup}"
            : backup is null ? "Nenhuma injeção ainda" : $"{backup} pronto";
    }

    private void ApplyContinue(ProjectDashboard dashboard)
    {
        var files = dashboard.Status.Files;
        var next = files.FirstOrDefault(f => f.Counts.Untranslated > 0)
            ?? files.FirstOrDefault(f => ProjectDashboard.AwaitingReview(f.Counts) > 0)
            ?? files.FirstOrDefault();
        continueKey = next?.Key;
        ContinueLabel = next is null ? string.Empty : $"Continuar em {next.Key}";
        OnPropertyChanged(nameof(CanContinue));
    }

    private void ApplyAi(ProjectSummary project, ProjectDashboard dashboard, StatusCounts total)
    {
        var usage = dashboard.AiUsage;
        var model = ProjectLabels.Model(project.Model);
        AiValue = Dollars(usage.Dollars);
        AiDescription = usage.Runs == 0
            ? $"Nenhuma chamada de IA registrada · {model}"
            : $"{TokensShort(usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens)} tokens de entrada · {TokensShort(usage.OutputTokens)} de saída · {model}";
        if (usage.UnpricedRuns > 0)
        {
            AiDescription += usage.UnpricedRuns >= usage.Runs
                ? " · custo desconhecido (modelo sem preço na tabela)"
                : $" · custo parcial ({Plural(usage.UnpricedRuns, "execução sem preço", "execuções sem preço")})";
        }

        AiLegend.Clear();
        AiLegend.Add(new LegendEntry($"Gasto {Dollars(usage.Dollars)}", LegendTone.Machine));
        if (EstimateRemaining(usage, total) is { } remaining)
        {
            AiLegend.Add(new LegendEntry($"Restante estimado {Dollars(remaining)}", LegendTone.Pending));
            var spentPercent = (int)Math.Clamp(Math.Round(usage.Dollars / (usage.Dollars + remaining) * 100m, MidpointRounding.AwayFromZero), 0m, 100m);
            AiDistribution = new StatusDistribution(0, 0, 0, spentPercent, 100 - spentPercent);
            AiBarDescription = $"Gasto {Dollars(usage.Dollars)} de {Dollars(usage.Dollars + remaining)} estimados, {spentPercent}%";
        }
        else
        {
            AiDistribution = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));
            AiBarDescription = $"Gasto {Dollars(usage.Dollars)}, sem estimativa do restante";
        }
    }

    private void ApplyConference(ProjectDashboard dashboard)
    {
        ConferenceLegend.Clear();
        if (dashboard.Qa is not { } qa)
        {
            ConferenceValue = "—";
            ConferenceDescription = "Não foi possível conferir o projeto";
            ConferenceErrors = ConferenceWarnings = 0;
            ConferenceBarDescription = ConferenceDescription;
            return;
        }

        ConferenceValue = qa.Total == 0 ? "Nenhum problema" : Plural(qa.Total, "problema", "problemas");
        ConferenceDescription = qa switch
        {
            { Errors: > 0 } => "Os erros bloqueiam o build",
            { Warnings: > 0 } => "Revise os avisos antes do build",
            _ => "Nada a corrigir",
        };
        ConferenceErrors = qa.Errors;
        ConferenceWarnings = qa.Warnings;
        ConferenceLegend.Add(new LegendEntry($"Erros {ProjectLabels.Number(qa.Errors)}", LegendTone.Error));
        ConferenceLegend.Add(new LegendEntry($"Avisos {ProjectLabels.Number(qa.Warnings)}", LegendTone.Warning));
        ConferenceBarDescription = $"{Plural(qa.Errors, "erro", "erros")} e {Plural(qa.Warnings, "aviso", "avisos")}";
    }

    private void ApplyTables(ProjectDashboard dashboard)
    {
        var files = dashboard.Status.Files;
        // Scripts com nome primeiro (todos, num jogo que a engine nomeia), cada grupo na ordem do jogo; só os primeiros cabem na tabela.
        var ordered = files.Where(f => dashboard.ScriptNames.DisplayName(f.Key) is not null)
            .Concat(files.Where(f => dashboard.ScriptNames.DisplayName(f.Key) is null))
            .Take(TableRows);
        Scripts.Clear();
        foreach (var file in ordered)
        {
            Scripts.Add(new ScriptRowViewModel(file.Key, dashboard.ScriptNames.DisplayName(file.Key), file.Counts));
        }

        ScriptsFooter = $"{Scripts.Count} de {Plural(files.Count, "script", "scripts")}";

        var characters = dashboard.Status.Characters;
        Characters.Clear();
        foreach (var character in characters.Take(TableRows))
        {
            string? translation = null;
            if (character.Speaker is { } speaker)
            {
                dashboard.CharacterNames.TryGetValue(speaker.Trim(), out translation);
            }

            Characters.Add(new CharacterRowViewModel(character.Speaker, translation, character.Counts));
        }

        CharactersFooter = $"{Characters.Count} de {Plural(characters.Count, "personagem", "personagens")}";
    }
}
