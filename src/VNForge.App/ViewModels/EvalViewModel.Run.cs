using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public sealed partial class EvalViewModel
{
    private int seed = 1;

    private CancellationTokenSource? runCancellation;
    private EvalEstimate? estimate;
    private decimal spentBrl;
    private int estimateVersion;

    /// <summary>O diálogo "Rodar o banco de provas" está aberto.</summary>
    [ObservableProperty]
    public partial bool IsRunOpen { get; set; }

    [ObservableProperty]
    public partial bool IsEstimating { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Onde a rodada em andamento está: <c>Configuração B (2 de 4): traduzindo...</c>.</summary>
    [ObservableProperty]
    public partial string RunStatus { get; private set; } = string.Empty;

    /// <summary>Com o juiz de IA cego, uma nota por texto.</summary>
    [ObservableProperty]
    public partial bool UseJudge { get; set; } = true;

    [ObservableProperty]
    public partial string RunCostText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RunSpentText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double SpentBrl { get; private set; }

    [ObservableProperty]
    public partial double AskBrl { get; private set; }

    [ObservableProperty]
    public partial double LimitBrl { get; private set; }

    [ObservableProperty]
    public partial string RunLabel { get; private set; } = "Rodar";

    [ObservableProperty]
    public partial bool CanRun { get; private set; }

    [ObservableProperty]
    public partial bool KeyMissing { get; private set; }

    [ObservableProperty]
    public partial bool IsOverLimit { get; private set; }

    [ObservableProperty]
    public partial bool AboveWarning { get; private set; }

    [ObservableProperty]
    public partial string RunError { get; private set; } = string.Empty;

    public string RunIntro => $"Cada configuração traduz as mesmas {EvalText.CountLines(Math.Min(DefaultSample, GoldLines))} aprovadas do projeto. O relatório mostra custo, tempo e qualidade, e você escolhe entre os pares às cegas.";

    private bool CanOpenRun => HasProject && HasGold && !IsRunning;

    /// <summary>Abre o diálogo e estima o custo de cada configuração fora da thread da tela.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenRun))]
    private Task RunOpen()
    {
        RunItems.Clear();
        estimate = null;
        seed = Random.Shared.Next(1, int.MaxValue);
        RunError = string.Empty;
        IsRunOpen = true;
        IsEstimating = true;
        Refresh();
        return EstimateAsync(++estimateVersion);
    }

    private async Task EstimateAsync(int mine)
    {
        var project = folder!;
        var selected = configs.Count > 0 ? configs : [.. EvalConfigParser.Defaults];
        var sample = Math.Min(DefaultSample, GoldLines);
        var drawSeed = seed;
        EvalEstimate? result = null;
        decimal spent = 0m;
        string? error = null;
        try
        {
            (result, spent) = await Task.Run(() => (
                EvalEstimator.Estimate(project, selected, sample, drawSeed, new TranslationRunOptions().BatchSize, JudgeModel),
                AiUsageLog.Load(project).Dollars), CancellationToken.None).ConfigureAwait(true);
        }
        catch (EvalException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = $"Não foi possível estimar o custo: {ex.Message}";
        }

        if (mine != estimateVersion)
        {
            return;
        }

        estimate = result;
        var rate = Context().Rate;
        spentBrl = rate.ToBrl(spent);
        RunItems.Clear();
        if (result is not null)
        {
            foreach (var item in result.Configs)
            {
                var row = new EvalRunItemViewModel(item.Config, rate.ToBrl(item.Estimate.Dollars), rate.ToBrl(item.JudgeDollars));
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(EvalRunItemViewModel.IsSelected))
                    {
                        Refresh();
                    }
                };
                RunItems.Add(row);
            }
        }

        RunError = error ?? string.Empty;
        IsEstimating = false;
        Refresh();
    }

    partial void OnUseJudgeChanged(bool value) => Refresh();

    private IReadOnlyList<EvalRunItemViewModel> Chosen => [.. RunItems.Where(i => i.IsSelected)];

    private void Refresh()
    {
        var context = Context();
        var chosen = Chosen;
        var ask = chosen.Sum(i => i.Brl + (UseJudge ? i.JudgeBrl : 0m));
        var limit = context.Limits.Limit;
        RunCostText = $"Custo (aproximado): {EvalText.Brl(ask)}";
        RunSpentText = limit is { } cap
            ? $"Já gasto no projeto: {EvalText.Brl(spentBrl)} de {EvalText.Brl(cap)}"
            : $"Já gasto no projeto: {EvalText.Brl(spentBrl)}";
        SpentBrl = (double)spentBrl;
        AskBrl = (double)ask;
        LimitBrl = (double)(limit ?? 0m);
        RunLabel = chosen.Count switch
        {
            0 => "Rodar",
            1 => "Rodar 1 configuração",
            _ => $"Rodar {chosen.Count} configurações",
        };
        KeyMissing = !context.HasApiKey;
        IsOverLimit = limit is { } max && spentBrl + ask > max;
        AboveWarning = !IsOverLimit && context.Limits.WarnAbove is { } warn && ask > warn;
        CanRun = !IsEstimating && estimate is not null && chosen.Count > 0 && !KeyMissing && !IsOverLimit && !IsRunning;
        StartRunCommand.NotifyCanExecuteChanged();
        RunOpenCommand.NotifyCanExecuteChanged();
    }

    public string OverLimitText => "Este pedido passaria do limite de gasto do projeto. Tire configurações ou aumente o limite nos Ajustes.";

    public string KeyMissingText => "Falta a chave da IA. Coloque a chave nos Ajustes para rodar as provas.";

    [RelayCommand]
    private void CloseRunDialog() => IsRunOpen = false;

    /// <summary>Roda as configurações marcadas; o diálogo fecha e a rodada segue em segundo plano, com o andamento na faixa de cima.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task StartRun()
    {
        var project = folder!;
        var chosen = Chosen.Select(i => i.Config).ToList();
        var letters = chosen.Select((c, i) => c with { Id = EvalConfig.LetterFor(i) }).ToList();
        var judge = UseJudge;
        IsRunOpen = false;
        IsRunning = true;
        Message = null;
        ErrorMessage = null;
        RunStatus = "Começando a rodada...";
        runCancellation = new CancellationTokenSource();
        var token = runCancellation.Token;
        RunOpenCommand.NotifyCanExecuteChanged();
        Running = RunCoreAsync(project, letters, judge, token);
        return Running;
    }

    private async Task RunCoreAsync(ProjectFolder project, List<EvalConfig> chosen, bool judge, CancellationToken token)
    {
        var context = Context();
        var request = new EvalRunRequest(project, chosen, Math.Min(DefaultSample, GoldLines), seed, Provider)
        {
            Limits = context.Limits,
            Rate = context.Rate,
            Judge = judge ? JudgeProviderFactory(JudgeModel) : null,
            JudgeModel = judge ? JudgeModel : null,
            Progress = new Progress<EvalProgress>(p => RunStatus = $"Configuração {p.ConfigId} ({p.ConfigNumber} de {p.ConfigCount}): {(p.Phase == EvalPhase.Translating ? "traduzindo" : "avaliando com o juiz")}..."),
        };
        try
        {
            round = await Task.Run(() => EvalRunner.RunAsync(request, token), token).ConfigureAwait(true);
            configs = [.. round.Reports.Select(r => r.Config)];
            IsBlind = false;
            Apply();
            Message = round?.PartialNote ?? $"A rodada terminou com {chosen.Count} {(chosen.Count == 1 ? "configuração" : "configurações")}.";
        }
        catch (OperationCanceledException)
        {
            Message = "A rodada foi cancelada. O que já foi gasto continua no gasto do projeto.";
        }
        catch (EvalException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"A rodada parou: {ClaudeErrorMessages.Describe(ex)}";
        }
        finally
        {
            runCancellation?.Dispose();
            runCancellation = null;
            IsRunning = false;
            RunStatus = string.Empty;
            RunOpenCommand.NotifyCanExecuteChanged();
        }
    }

    private EvalProvider Provider(EvalConfig config)
    {
        var effort = ClaudeEffortText.ParseOrDefault(config.Effort);
        var translator = TranslationProviderFactory(config.Model, effort);
        return new EvalProvider(translator, () => (translator as ClaudeTranslationProvider)?.Usage);
    }

    [RelayCommand]
    private void CancelRun() => runCancellation?.Cancel();
}
