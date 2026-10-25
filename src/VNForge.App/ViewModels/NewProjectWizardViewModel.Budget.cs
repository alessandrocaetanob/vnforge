using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Engines;

namespace VNForge.App.ViewModels;

/// <summary>O passo "IA e gasto": as três qualidades com o custo do jogo inteiro, a cotação do dólar e o limite de gasto.</summary>
public sealed partial class NewProjectWizardViewModel
{
    private AppSettings saved = AppSettings.Default;
    private string initialRateText = string.Empty;
    private string initialLimitText = string.Empty;
    private IReadOnlyDictionary<string, decimal> dollarsByModel = new Dictionary<string, decimal>();

    /// <summary>Rápida, Equilibrada e Caprichada; marcar uma troca o modelo do projeto e o contrário também.</summary>
    public ReadOnlyCollection<QualityOption> Qualities { get; private set; } = new([]);

    /// <summary>Linhas esperadas no jogo inteiro (veja <see cref="GameCostEstimator"/>); nulo quando a detecção não contou os scripts.</summary>
    public int? EstimatedLines { get; private set; }

    /// <summary>Reais por dólar como a pessoa digitou; vazio usa o valor de exemplo.</summary>
    [ObservableProperty]
    public partial string RateText { get; set; } = string.Empty;

    /// <summary>Limite de gasto em reais; vazio é "sem limite".</summary>
    [ObservableProperty]
    public partial string LimitText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? RateError { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? LimitError { get; private set; }

    /// <summary>A cotação das contas do passo: a digitada, ou o valor de exemplo enquanto a pessoa não informa.</summary>
    [ObservableProperty]
    public partial ExchangeRate Rate { get; private set; } = ExchangeRate.Placeholder;

    [ObservableProperty]
    public partial string RateNote { get; private set; } = string.Empty;

    /// <summary>De onde vem a conta do jogo inteiro.</summary>
    [ObservableProperty]
    public partial string EstimateNote { get; private set; } = string.Empty;

    public const string KeyNote = "A chave de uso do Claude não entra no projeto. Ela fica guardada no Windows, só no seu usuário, e você a cadastra em Ajustes.";

    /// <summary>A cotação, o limite e a qualidade foram gravados no <c>app.json</c>; quem guarda uma cópia dos ajustes do usuário deve relê-los.</summary>
    public event EventHandler? BudgetSaved;

    private string SelectedQualityTitle =>
        QualityProfiles.ForModel(Models.Selected.Id) is { } profile
            ? Qualities.Single(q => q.Profile == profile).Title
            : "Modelo escolhido à mão";

    partial void OnRateTextChanged(string value) => RefreshRate();

    partial void OnLimitTextChanged(string value)
    {
        BudgetInput.TryReadLimit(value, out _, out var error);
        LimitError = error;
    }

    private void InitializeBudget()
    {
        Qualities = new ReadOnlyCollection<QualityOption>([.. QualityProfiles.All.Select(QualityOption.Describe)]);
        foreach (var quality in Qualities)
        {
            quality.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(QualityOption.IsSelected) && quality.IsSelected)
                {
                    Models.Select(quality.Profile.Model);
                }
            };
        }

        Models.SelectionChanged += (_, _) => MarkQualityOfModel();

        saved = appSettings?.Load() ?? AppSettings.Default;
        initialRateText = saved.BrlPerUsd is { } rate ? MoneyParser.Format(rate) : string.Empty;
        initialLimitText = saved.SpendLimit is { } limit ? MoneyParser.Format(limit) : string.Empty;
        RateText = initialRateText;
        LimitText = initialLimitText;
        Models.Select(QualityProfiles.For(saved.Quality).Model);
        MarkQualityOfModel();
        RefreshRate();
        ShowGameSize(null, null);
    }

    /// <summary>A qualidade marcada é a que usa o modelo escolhido; nenhuma quando é o Fable, que só existe nos detalhes técnicos.</summary>
    private void MarkQualityOfModel()
    {
        var profile = QualityProfiles.ForModel(Models.Selected.Id);
        foreach (var quality in Qualities)
        {
            quality.IsSelected = quality.Profile == profile;
        }
    }

    private void RefreshRate()
    {
        if (!BudgetInput.TryReadRate(RateText, out var rate, out var error))
        {
            RateError = error;
            return;
        }

        RateError = null;
        // Enquanto a pessoa não mexe no campo, vale o número salvo, não o texto arredondado em duas casas.
        rate = RateText == initialRateText ? saved.BrlPerUsd : rate;
        Rate = rate is { } value ? new ExchangeRate(value, KeepsSavedDate(value) ?? Today()) : ExchangeRate.Placeholder;
        RateNote = Rate.IsInformed
            ? "Fica salva nos Ajustes e vale para as contas em reais do app. O app não consulta a cotação na internet."
            : $"Ainda não informada: as contas usam {Money.Brl(ExchangeRate.Placeholder.BrlPerUsd)} por dólar, só como exemplo. Informe a de hoje para a conta ficar certa.";
        ApplyEstimates();
    }

    /// <summary>A cotação que a pessoa não mexeu continua com a data em que foi informada.</summary>
    private DateOnly? KeepsSavedDate(decimal rate) => saved.BrlPerUsd == rate ? saved.RateInformedOn : null;

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private void ShowGameSize(int? scripts, IEngine? engine)
    {
        ScriptCount = scripts;
        EstimatedLines = GameCostEstimator.LinesFor(scripts);
        dollarsByModel = EstimatedLines is { } lines
            ? GameCostEstimator.DollarsByModel(lines, QualityProfiles.All.Select(p => p.Model))
            : new Dictionary<string, decimal>();
        EstimateNote = EstimatedLines is { } expected && scripts is { } count
            ? $"Estimativa para o jogo inteiro, a partir de {ProjectLabels.ScriptFiles(count)} (cerca de {ProjectLabels.Number(expected)} linhas). O valor certo aparece depois da extração do texto."
            : "O gasto do jogo inteiro aparece depois da extração do texto.";
        RecommendAccentAdjustment(engine);
        ApplyEstimates();
    }

    private void ApplyEstimates()
    {
        foreach (var quality in Qualities)
        {
            var dollars = dollarsByModel.TryGetValue(quality.Profile.Model, out var found) ? found : 0m;
            quality.ShowEstimate(EstimatedLines, Rate.ToBrl(dollars), Rate);
        }
    }

    /// <summary>
    /// Grava a cotação, o limite e a qualidade escolhida no <c>app.json</c> do usuário, pelo mesmo arquivo dos Ajustes; sem ele, não grava nada.
    /// Cotação e limite que a pessoa não mexeu ficam como estavam no arquivo: o texto do campo tem duas casas e não pode arredondar o valor salvo.
    /// </summary>
    private void SaveBudget()
    {
        if (appSettings is null)
        {
            return;
        }

        try
        {
            var current = appSettings.Load();
            var quality = QualityProfiles.ForModel(Models.Selected.Id)?.Quality;
            var updated = current with { Quality = quality ?? current.Quality };
            if (RateText != initialRateText)
            {
                BudgetInput.TryReadRate(RateText, out var rate, out _);
                updated = updated with { BrlPerUsd = rate, RateInformedOn = rate is null ? null : Rate.InformedOn };
            }

            if (LimitText != initialLimitText)
            {
                BudgetInput.TryReadLimit(LimitText, out var limit, out _);
                updated = updated with { SpendLimit = limit };
            }

            appSettings.Save(updated);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"O projeto foi criado, mas não deu para salvar a cotação e o limite: {e.Message} Ajuste em Ajustes.";
            return;
        }

        NotifyBudgetSaved();
    }

    /// <summary>O arquivo já foi gravado e o projeto já existe: uma falha ao reler os ajustes não volta a pessoa ao passo do nome.</summary>
    private void NotifyBudgetSaved()
    {
        try
        {
            BudgetSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (IsExpected(e))
        {
            ErrorMessage = $"O projeto foi criado e a cotação e o limite foram salvos, mas o app não conseguiu recarregar os Ajustes: {e.Message} Feche e abra o app.";
        }
    }
}
