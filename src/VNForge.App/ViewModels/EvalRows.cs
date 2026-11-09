using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.Core.Eval;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha da tabela de provas: uma configuração com o que a rodada mediu dela.</summary>
public sealed partial class EvalRowViewModel : ObservableObject
{
    public EvalRowViewModel(EvalConfigReport report, EvalRound round, ExchangeRate rate)
    {
        Report = report;
        Letter = report.Config.Id;
        ModelName = report.Config.ModelName;
        Detail = report.Config.Detail;
        var (normal, batch) = EvalText.GameCost(report, round.RemainingLines, rate);
        CostText = EvalText.Brl(normal);
        BatchText = $"lote {EvalText.Brl(batch)}";
        TimeText = EvalText.Duration(report.ProjectedTime(round.RemainingLines));
        ChrfText = EvalText.Score(report.MeanChrf);
        JudgeText = report.MeanJudge is { } judge ? EvalText.Score(judge) : "sem juiz";
        BlindText = EvalText.Blind(round.BlindScore(Letter), round.PairsDone > 0);
        IsChosen = round.ChosenConfigId == Letter;
        ProjectedBrl = (double)normal;
        Chrf = report.MeanChrf;
        Note = report.RefusedLines > 0 ? $"{report.RefusedLines} {(report.RefusedLines == 1 ? "linha ficou" : "linhas ficaram")} sem tradução" : report.Error;
    }

    public EvalConfigReport Report { get; }

    public string Letter { get; }

    public string ModelName { get; }

    public string Detail { get; }

    public string CostText { get; }

    public string BatchText { get; }

    public string TimeText { get; }

    public string ChrfText { get; }

    public string JudgeText { get; }

    public string BlindText { get; }

    /// <summary>A configuração que a pessoa escolheu para o M4: ganha o selo e a linha em <c>accent-soft</c>.</summary>
    public bool IsChosen { get; }

    /// <summary>O que a pessoa deve saber da configuração (linhas que ficaram sem tradução, erro de lote); nulo quando não há nada.</summary>
    public string? Note { get; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public double ProjectedBrl { get; }

    public double Chrf { get; }

    /// <summary>A linha que a pessoa marcou para o botão "Usar X no M4".</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Uma linha do diálogo "Rodar o banco de provas": a configuração, o custo dela na amostra e se entra na rodada.</summary>
public sealed partial class EvalRunItemViewModel(EvalConfig config, decimal brl, decimal judgeBrl) : ObservableObject
{
    public EvalConfig Config { get; } = config;

    public string Letter { get; } = config.Id;

    /// <summary><c>Opus 5.5 · effort alto · com bíblia</c>.</summary>
    public string Title { get; } = config.Title;

    /// <summary><c>15 falas de contexto</c>.</summary>
    public string Detail { get; } = config.ContextText;

    /// <summary>O custo da configuração na amostra, sem o juiz.</summary>
    public decimal Brl { get; } = brl;

    /// <summary>O custo do juiz sobre as respostas da configuração.</summary>
    public decimal JudgeBrl { get; } = judgeBrl;

    public string CostText { get; } = EvalText.Brl(brl);

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;
}

/// <summary>Um ponto do gráfico de custo contra chrF.</summary>
public sealed record EvalChartPoint(string Letter, double Cost, double Chrf, bool IsChosen);
