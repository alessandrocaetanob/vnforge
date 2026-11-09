using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// A aba "Provas" do Painel (banco de provas, #529): o relatório da última rodada (custo do jogo todo, tempo, chrF, nota do juiz de IA e a escolha às cegas de cada
/// configuração), o gráfico de custo contra chrF, a rodada às cegas e o diálogo que roda as provas. Todo número vem do Core e do <c>VNForge.AI</c>
/// (<see cref="EvalRunner"/>, <see cref="EvalReading"/>); aqui só se mostra, e nenhuma tradução é gravada no projeto.
/// </summary>
public sealed partial class EvalViewModel : ObservableObject
{
    public const int DefaultSample = 200;

    private readonly TimeProvider clock;
    private ProjectFolder? folder;
    private EvalRound? round;
    private IReadOnlyList<EvalConfig> configs = [];
    private int version;

    public EvalViewModel(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        RunItems = [];
    }

    /// <summary>A cotação, o limite de gasto e se há chave; o Painel passa o mesmo contexto que usa no resto da tela.</summary>
    public Func<DashboardContext> Context { get; set; } = () => DashboardContext.Default;

    /// <summary>Monta o provedor de tradução da configuração; o app passa o Claude com a chave do usuário e os testes, o provedor falso.</summary>
    public Func<string, ClaudeEffort, ITranslationProvider> TranslationProviderFactory { get; set; } =
        static (_, _) => throw new InvalidOperationException("Nenhum provedor de IA configurado.");

    /// <summary>Monta o juiz de IA para o modelo escolhido.</summary>
    public Func<string, IJudgeProvider> JudgeProviderFactory { get; set; } =
        static _ => throw new InvalidOperationException("Nenhum juiz de IA configurado.");

    /// <summary>O modelo do juiz de IA.</summary>
    public string JudgeModel { get; set; } = ClaudeModels.Sonnet;

    /// <summary>A pessoa clicou em "Rodar no terminal": a tela copia <see cref="TerminalCommand"/>.</summary>
    public event EventHandler? TerminalCommandRequested;

    /// <summary>Um aviso da chave ou do limite pede os Ajustes.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>O comando que faz a mesma rodada no terminal.</summary>
    public string TerminalCommand => "vnf eval";

    /// <summary>Termina quando a última leitura do projeto está na tela.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Termina quando a última rodada pedida acabou.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    public ObservableCollection<EvalRowViewModel> Rows { get; } = [];

    public ObservableCollection<EvalChartPoint> Points { get; } = [];

    public ObservableCollection<EvalRunItemViewModel> RunItems { get; }

    [ObservableProperty]
    public partial bool HasProject { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Quantas linhas aprovadas o projeto tem.</summary>
    [ObservableProperty]
    public partial int GoldLines { get; private set; }

    [ObservableProperty]
    public partial string GoldText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SampleText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int ConfigCount { get; private set; }

    public bool HasGold => GoldLines > 0;

    /// <summary>Há linhas aprovadas, mas nenhuma rodada ainda.</summary>
    public bool ShowNoRound => HasProject && !IsLoading && HasGold && !HasRound;

    public bool ShowNoGold => HasProject && !IsLoading && !HasGold;

    /// <summary>A faixa do conjunto ouro e dos botões de rodar.</summary>
    public bool ShowStrip => HasProject && !IsLoading && HasGold && !IsBlind;

    public string NoGoldText => "O banco de provas compara as traduções com as linhas que você aprovou. Aprove algumas no Editor e volte aqui.";

    public string NoRoundText => "Ainda não há rodada neste projeto. Rode as provas para ver custo, tempo e qualidade de cada configuração lado a lado.";

    [ObservableProperty]
    public partial bool HasRound { get; private set; }

    /// <summary>O relatório está na tela: há rodada e a rodada às cegas não a cobre.</summary>
    public bool ShowReport => HasRound && !IsBlind;

    [ObservableProperty]
    public partial string RoundTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RoundSubtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Footnote { get; private set; } = string.Empty;

    /// <summary>"Leitura da rodada": a frase que o Core escreve sobre os números.</summary>
    [ObservableProperty]
    public partial string Reading { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BlindSummary { get; private set; } = string.Empty;

    /// <summary>Os pares respondidos sobre o total, de 0 a 1, para a barra de "Rodada às cegas".</summary>
    [ObservableProperty]
    public partial double BlindFraction { get; private set; }

    public bool HasPairs => round is { Pairs.Count: > 0 };

    /// <summary>"Continuar às cegas" enquanto há par sem resposta; "Começar às cegas" na primeira vez.</summary>
    [ObservableProperty]
    public partial string BlindActionText { get; private set; } = "Começar às cegas";

    [ObservableProperty]
    public partial bool CanBlind { get; private set; }

    /// <summary>O texto do botão "Usar C no M4"; vazio sem configuração para sugerir.</summary>
    [ObservableProperty]
    public partial string UseLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanUse { get; private set; }

    /// <summary>Um aviso curto sobre a última ação (a rodada que parou, a que acabou).</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Lê a rodada, as configurações e as linhas aprovadas do projeto fora da thread da tela.</summary>
    public Task LoadAsync(ProjectFolder? project)
    {
        folder = project;
        Message = null;
        ErrorMessage = null;
        IsBlind = false;
        HasProject = project is not null;
        if (project is null)
        {
            round = null;
            configs = [];
            GoldLines = 0;
            Apply();
            Loading = Task.CompletedTask;
            return Loading;
        }

        IsLoading = true;
        Loading = ReadAsync(project, ++version);
        return Loading;
    }

    private async Task ReadAsync(ProjectFolder project, int mine)
    {
        EvalRound? latest = null;
        IReadOnlyList<EvalConfig> saved = [];
        var gold = 0;
        string? error = null;
        try
        {
            (latest, saved, gold) = await Task.Run(() => (EvalStore.LoadLatest(project), EvalStore.LoadConfigs(project), GoldSet.From(project.LoadAllText()).Count), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = $"Não foi possível ler o projeto: {ex.Message}";
        }

        if (mine != version)
        {
            return;
        }

        round = latest;
        configs = saved.Count > 0 ? saved : (latest?.Reports.Select(r => r.Config).ToList() ?? [.. EvalConfigParser.Defaults]);
        GoldLines = gold;
        ErrorMessage = error;
        IsLoading = false;
        Apply();
    }

    private void Apply()
    {
        var rate = Context().Rate;
        HasRound = round is not null;
        Rows.Clear();
        Points.Clear();
        GoldText = GoldLines == 1 ? "1 linha aprovada" : $"{GoldLines.ToString("N0", Pt)} linhas aprovadas";
        ConfigCount = configs.Count;
        SampleText = EvalText.CountLines(round?.Sample.Count ?? Math.Min(DefaultSample, GoldLines));
        if (round is { } current)
        {
            foreach (var report in current.Reports)
            {
                Rows.Add(new EvalRowViewModel(report, current, rate));
            }

            foreach (var row in Rows)
            {
                Points.Add(new EvalChartPoint(row.Letter, row.ProjectedBrl, row.Chrf, row.IsChosen));
            }

            RoundTitle = $"Rodada de {current.At.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture)}";
            RoundSubtitle = $"{EvalText.CountLines(current.Sample.Count)} sorteadas do conjunto ouro · mesma amostra em todas as configurações";
            Footnote = $"O custo do jogo todo usa o preço de hoje e vale para as {EvalText.CountLines(current.RemainingLines)} que faltam.";
            Reading = EvalReading.Sentence(current);
            var total = current.Pairs.Count;
            BlindSummary = total == 0 ? "A rodada não gerou pares para comparar às cegas." : $"{current.PairsDone} de {total} pares feitos";
            BlindFraction = total == 0 ? 0 : (double)current.PairsDone / total;
            CanBlind = current.NextPair is not null;
            BlindActionText = current.PairsDone == 0 ? "Começar às cegas" : "Continuar às cegas";
            var target = UseTarget();
            UseLabel = target is null ? string.Empty : $"Usar {target} no M4";
            CanUse = target is not null;
        }
        else
        {
            RoundTitle = string.Empty;
            RoundSubtitle = string.Empty;
            Footnote = string.Empty;
            Reading = string.Empty;
            BlindSummary = string.Empty;
            BlindFraction = 0;
            CanBlind = false;
            UseLabel = string.Empty;
            CanUse = false;
        }

        OnPropertyChanged(nameof(HasGold));
        OnPropertyChanged(nameof(HasPairs));
        OnPropertyChanged(nameof(ShowNoRound));
        OnPropertyChanged(nameof(ShowNoGold));
        OnPropertyChanged(nameof(ShowStrip));
        OnPropertyChanged(nameof(ShowReport));
        RunOpenCommand.NotifyCanExecuteChanged();
        BlindStartCommand.NotifyCanExecuteChanged();
        UseCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasRoundChanged(bool value) => OnPropertyChanged(nameof(ShowNoRound));

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoRound));
        OnPropertyChanged(nameof(ShowNoGold));
        OnPropertyChanged(nameof(ShowStrip));
    }

    partial void OnHasProjectChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoRound));
        OnPropertyChanged(nameof(ShowNoGold));
        OnPropertyChanged(nameof(ShowStrip));
    }

    /// <summary>A letra que o botão "Usar" marca: a linha que a pessoa clicou, senão a escolhida antes, senão a que o Core sugere.</summary>
    private string? UseTarget() =>
        Rows.FirstOrDefault(r => r.IsSelected)?.Letter ?? round?.ChosenConfigId ?? (round is null ? null : EvalReading.Recommended(round)?.Config.Id);

    /// <summary>Marca a linha como a que o botão "Usar" aplica.</summary>
    [RelayCommand]
    private void SelectRow(EvalRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var other in Rows)
        {
            other.IsSelected = ReferenceEquals(other, row);
        }

        var target = UseTarget();
        UseLabel = target is null ? string.Empty : $"Usar {target} no M4";
        CanUse = target is not null;
        UseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"Usar C no M4": grava a escolha na rodada; a linha ganha o selo.</summary>
    [RelayCommand(CanExecute = nameof(CanUse))]
    private void Use()
    {
        if (round is null || folder is null || UseTarget() is not { } letter)
        {
            return;
        }

        round = round.WithChosen(letter);
        Save();
        try
        {
            EvalChoice.Apply(folder, round.Reports.First(r => r.Config.Id == letter).Config);
        }
        catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"A escolha ficou na rodada, mas o vnforge.json não pôde ser gravado: {ex.Message}";
        }

        Message = $"A configuração {letter} ficou como a escolhida para o M4: o projeto passa a usar o modelo, o effort e o contexto dela.";
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }

        Apply();
    }

    [RelayCommand]
    private void CopyTerminalCommand() => TerminalCommandRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void Save()
    {
        if (round is null || folder is null)
        {
            return;
        }

        try
        {
            EvalStore.SaveRound(folder, round);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível gravar a rodada: {ex.Message}";
        }
    }

    private static CultureInfo Pt { get; } = CultureInfo.GetCultureInfo("pt-BR");
}
