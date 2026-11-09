using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Review;

namespace VNForge.App.ViewModels;

/// <summary>
/// A fila por risco de uma sessão do modo revisão: as linhas pontuadas (<see cref="Queue"/>), a mesma fila como itens de revisão e o ritmo da pessoa.
/// A contagem de linhas de risco alto que faltam é guardada de trás para frente, então cada linha nova a lê sem percorrer a fila.
/// </summary>
public sealed class RiskReview
{
    private readonly int[] highFrom;

    public RiskReview(RiskQueue queue, ReviewPace pace)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(pace);
        Queue = queue;
        Pace = pace;
        Items = [.. queue.Lines.Select(l => new ReviewItem(l.Line.ScriptKey, l.Line.Number, l.Line.Unit.Id))];
        highFrom = new int[queue.Lines.Count + 1];
        for (var i = queue.Lines.Count - 1; i >= 0; i--)
        {
            highFrom[i] = highFrom[i + 1] + (queue.Lines[i].Level == RiskLevel.High ? 1 : 0);
        }
    }

    public RiskQueue Queue { get; }

    public ReviewPace Pace { get; }

    public IReadOnlyList<ReviewItem> Items { get; }

    /// <summary>Quantas linhas de risco alto há da posição <paramref name="index"/> em diante.</summary>
    public int HighFrom(int index) => highFrom[Math.Clamp(index, 0, highFrom.Length - 1)];
}

/// <summary>Um sinal do cartão de risco: o texto do chip ("Dúvida da IA +30") e a cor dele.</summary>
public sealed record RiskChip(string Text, bool IsAi, bool IsWarning);

/// <summary>Uma linha da lista "A seguir" da fila por risco.</summary>
public sealed record RiskRow(string Score, string LevelText, bool IsHigh, bool IsMedium, string LineLabel, string Speaker, string Text, string Signals, bool IsCurrent);

public sealed partial class ReviewModeViewModel
{
    private const int UpNextCount = 4;

    private readonly RiskReview? risk;

    /// <summary>A fila é a do risco: a tela mostra o cartão de risco da linha e a lista do que vem a seguir.</summary>
    public bool IsRisk => risk is not null;

    /// <summary>Os blocos do topo: um por linha, menos na fila por risco, que passa de milhares de linhas e não cabe numa faixa.</summary>
    public IReadOnlyList<ReviewBlock> VisibleBlocks => risk is null ? Blocks : [];

    /// <summary>O cartão de risco e a fila aparecem enquanto há uma linha em exibição.</summary>
    public bool ShowRisk => risk is not null && IsActive;

    /// <summary>A pessoa pode trocar a ordem ("Do começo" ou "O que mais precisa de você"): só nas filas da cena aberta e do risco.</summary>
    public bool HasOrderChoice => source.Kind is ReviewSourceKind.OpenScene or ReviewSourceKind.Risk;

    /// <summary>A ordem da fila: 0 é "Do começo" (a cena aberta), 1 é "O que mais precisa de você". Trocar abre a outra fila.</summary>
    public int OrderIndex
    {
        get => source.Kind == ReviewSourceKind.Risk ? 1 : 0;
        set
        {
            if (!HasOrderChoice || value == OrderIndex || value is < 0 or > 1)
            {
                return;
            }

            if (value == 1)
            {
                editor.StartRiskReview();
            }
            else
            {
                editor.StartReview();
            }

            // Sem linhas esperando revisão a fila nova não abre: o segmento volta para a ordem que continua valendo.
            OnPropertyChanged(nameof(OrderIndex));
        }
    }

    private LineRisk? CurrentRisk => risk is not null && !IsFinished && Position < risk.Queue.Lines.Count ? risk.Queue.Lines[Position] : null;

    /// <summary>"Risco 92".</summary>
    public string RiskScoreText => CurrentRisk is { } current ? $"Risco {current.Score}" : string.Empty;

    public string RiskLevelText => CurrentRisk is { } current ? LevelLabel(current.Level) : string.Empty;

    public bool IsHighRisk => CurrentRisk?.Level == RiskLevel.High;

    public bool IsMediumRisk => CurrentRisk?.Level == RiskLevel.Medium;

    /// <summary>"linha 0495 · 1ª da fila".</summary>
    public string RiskPositionText => CurrentRisk is { } current ? $"linha {current.Line.Number:D4} · {Position + 1}ª da fila" : string.Empty;

    /// <summary>Os sinais que somaram na linha em exibição, um chip cada; vazio quando a linha não tem nenhum.</summary>
    public IReadOnlyList<RiskChip> RiskChips =>
        CurrentRisk is { } current
            ? [.. current.Factors.Select(f => new RiskChip(f.Text, f.Kind is RiskFactorKind.AiDoubt or RiskFactorKind.EditorSuggestion, f.Kind is RiskFactorKind.QaIssue or RiskFactorKind.SoundsOdd))]
            : [];

    public bool HasRiskChips => RiskChips.Count > 0;

    /// <summary>"312 linhas de risco alto · faltam cerca de 3 h 43 min no seu ritmo de 84 linhas por hora", das linhas de risco alto que ainda não passaram.</summary>
    public string RiskSummaryText => risk is { } r ? RiskQueue.DescribeHigh(IsFinished ? 0 : r.HighFrom(Position), r.Pace) : string.Empty;

    /// <summary>Quanto da fila é de cada nível, calculado quando a fila foi montada.</summary>
    public RiskDistribution RiskShare => risk?.Queue.Distribution ?? new RiskDistribution(0, 0, 100);

    public IReadOnlyList<LegendEntry> RiskLegend =>
    [
        new($"Alto {RiskShare.High}%", LegendTone.Error),
        new($"Médio {RiskShare.Medium}%", LegendTone.Warning),
        new($"Baixo {RiskShare.Low}%", LegendTone.Pending),
    ];

    /// <summary>A linha em exibição e as que vêm depois dela, da fila por risco.</summary>
    public IReadOnlyList<RiskRow> UpNext
    {
        get
        {
            if (risk is not { } r || IsFinished)
            {
                return [];
            }

            return [.. r.Queue.Lines.Skip(Position).Take(UpNextCount).Select((line, i) => RowOf(line, i == 0))];
        }
    }

    private RiskRow RowOf(LineRisk line, bool current)
    {
        var unit = editor.EntryOf(new ReviewItem(line.Line.ScriptKey, line.Line.Number, line.Line.Unit.Id))?.Unit ?? line.Line.Unit;
        var speaker = unit.EffectiveSpeaker;
        return new RiskRow(
            line.Score.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LevelLabel(line.Level),
            line.Level == RiskLevel.High,
            line.Level == RiskLevel.Medium,
            $"{line.Line.Number:D4}",
            string.IsNullOrEmpty(speaker) ? "Narração" : editor.DisplayNameOf(speaker),
            LineText.Plain(string.IsNullOrEmpty(unit.Translation) ? unit.Source : unit.Translation),
            string.Join(" · ", line.Factors.Select(f => f.ShortLabel)),
            current);
    }

    private static string LevelLabel(RiskLevel level) => level switch
    {
        RiskLevel.High => "Alto",
        RiskLevel.Medium => "Médio",
        _ => "Baixo",
    };

    private void NotifyRisk()
    {
        if (risk is null)
        {
            return;
        }

        OnPropertyChanged(nameof(RiskScoreText));
        OnPropertyChanged(nameof(RiskLevelText));
        OnPropertyChanged(nameof(IsHighRisk));
        OnPropertyChanged(nameof(IsMediumRisk));
        OnPropertyChanged(nameof(RiskPositionText));
        OnPropertyChanged(nameof(RiskChips));
        OnPropertyChanged(nameof(HasRiskChips));
        OnPropertyChanged(nameof(RiskSummaryText));
        OnPropertyChanged(nameof(UpNext));
    }
}
