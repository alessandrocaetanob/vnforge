using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O que a janela precisa saber dos Ajustes: a cotação, o limite e o aviso de gasto, a qualidade padrão, se há chave e por que o <c>app.json</c> não leu (o limite fica desconhecido).</summary>
public sealed record TranslateDialogSettings(ExchangeRate Rate, BudgetLimits Limits, TranslationQuality DefaultQuality, bool KeyConfigured, string? BudgetProblem = null);

/// <summary>Uma resposta à pergunta "o que traduzir": esta cena, o capítulo ou o jogo inteiro, com as linhas que ainda faltam em cada um.</summary>
public sealed partial class ScopeChoice(TranslationScope scope, string title) : ObservableObject
{
    public TranslationScope Scope { get; } = scope;

    public string Title { get; } = title;

    [ObservableProperty]
    public partial int Lines { get; internal set; }

    [ObservableProperty]
    public partial string Summary { get; internal set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Falso quando o escopo não tem linha pendente: o cartão fica desligado, com "Nada para traduzir aqui". Verdadeiro enquanto o projeto é lido.</summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; internal set; } = true;
}

/// <summary>Uma resposta à pergunta "com que qualidade": o preço e o tempo do escopo escolhido. O modelo não aparece: fica em Detalhes técnicos dos Ajustes.</summary>
public sealed partial class QualityChoice(TranslationQuality quality, string title, string description) : ObservableObject
{
    public TranslationQuality Quality { get; } = quality;

    public string Title { get; } = title;

    public string Description { get; } = description;

    [ObservableProperty]
    public partial decimal Price { get; internal set; }

    [ObservableProperty]
    public partial string Duration { get; internal set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// A janela "Traduzir com IA", sobre o Editor: o que traduzir, com que qualidade e por quanto. O preço e o tempo saem do Core
/// (<see cref="TranslationQuoter"/>); a janela só os mostra e impede o pedido que passa do limite de gasto do projeto.
/// </summary>
public sealed partial class TranslateDialogViewModel : ObservableObject
{
    private readonly ScopeChoice sceneChoice;
    private readonly ObservableCollection<ScopeChoice> scopes;
    private readonly Dictionary<TranslationScope, (IReadOnlyList<string> Keys, TranslationQuotes Quotes)> byScope = [];
    private TranslateDialogSettings settings = new(ExchangeRate.Placeholder, BudgetLimits.None, TranslationQuality.Careful, true);
    private TranslationContextSummary context = new(0, 0, 0, 0, false);
    private decimal spent;
    private int version;

    public TranslateDialogViewModel()
    {
        sceneChoice = Hooked(new(TranslationScope.Scene, "Esta cena"));
        scopes = [sceneChoice, Hooked(new(TranslationScope.Chapter, "O capítulo")), Hooked(new(TranslationScope.Game, "O jogo todo"))];
        Scopes = new(scopes);
        Qualities =
        [
            new(TranslationQuality.Fast, "Rápida", "Rascunho barato e veloz, para ler a história e revisar com calma."),
            new(TranslationQuality.Balanced, "Equilibrada", "O meio-termo: boa para a maior parte do jogo."),
            new(TranslationQuality.Careful, "Caprichada", "A mais fiel ao tom e aos personagens."),
        ];
        SelectedScope = Scopes[0];
        SelectedQuality = Qualities[^1];
        foreach (var quality in Qualities)
        {
            quality.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(QualityChoice.IsSelected) && quality.IsSelected)
                {
                    SelectedQuality = quality;
                }
            };
        }
    }

    /// <summary>A pessoa confirmou: quem sabe traduzir assume o pedido.</summary>
    public event EventHandler<TranslationRequest>? StartRequested;

    /// <summary>"Definir agora": a janela fecha e o app abre o Glossário na aba Personagens.</summary>
    public event EventHandler? GlossaryRequested;

    /// <summary>O link "Ajustes" do aviso de chave: a janela fecha e o app abre os Ajustes.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Esta cena (ou, com várias cenas marcadas na lista, as cenas escolhidas), o capítulo e o jogo todo.</summary>
    public ReadOnlyObservableCollection<ScopeChoice> Scopes { get; }

    public ReadOnlyCollection<QualityChoice> Qualities { get; }

    /// <summary>Termina quando os números da última abertura estão na tela.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial ScopeChoice SelectedScope { get; set; }

    [ObservableProperty]
    public partial QualityChoice SelectedQuality { get; set; }

    /// <summary>As linhas que o pedido traduz.</summary>
    [ObservableProperty]
    public partial int Lines { get; private set; }

    [ObservableProperty]
    public partial decimal RequestBrl { get; private set; }

    [ObservableProperty]
    public partial double SpentBrl { get; private set; }

    [ObservableProperty]
    public partial double AskBrl { get; private set; }

    /// <summary>O limite do projeto em reais; zero quando não há limite.</summary>
    [ObservableProperty]
    public partial double LimitBrl { get; private set; }

    [ObservableProperty]
    public partial string RequestText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SpentText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RateNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOverLimit { get; private set; }

    [ObservableProperty]
    public partial string OverLimitText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanSwitchToFit { get; private set; }

    [ObservableProperty]
    public partial string SwitchToFitText { get; private set; } = string.Empty;

    /// <summary>O pedido passa do valor em que o app pede um cuidado a mais (Ajustes), mas cabe no limite.</summary>
    [ObservableProperty]
    public partial bool AboveWarning { get; private set; }

    /// <summary>O <c>app.json</c> não pôde ser lido: o limite de gasto é desconhecido e a pessoa confirma antes de seguir.</summary>
    [ObservableProperty]
    public partial bool HasBudgetProblem { get; private set; }

    [ObservableProperty]
    public partial string BudgetProblemText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StartLabel { get; private set; } = "Traduzir";

    [ObservableProperty]
    public partial bool CanStart { get; private set; }

    /// <summary>Por que o projeto não pôde ser lido; vazio quando a leitura deu certo.</summary>
    [ObservableProperty]
    public partial string LoadError { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool NothingToTranslate { get; private set; }

    [ObservableProperty]
    public partial string ContextText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMissingGender { get; private set; }

    [ObservableProperty]
    public partial string MissingGenderText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool KeyMissing { get; private set; }

    [ObservableProperty]
    public partial string KeyMissingText { get; private set; } = string.Empty;

    /// <summary>Abre a janela e lê o projeto fora da thread da tela; a janela já aparece, com os números em branco, enquanto a leitura roda.</summary>
    /// <param name="sceneKey">A chave do script aberto no editor; <see langword="null"/> deixa só o jogo inteiro com linhas.</param>
    /// <param name="chosenKeys">Os scripts marcados na lista de cenas; com mais de um, "As cenas escolhidas" ocupa o lugar de "Esta cena".</param>
    public Task OpenAsync(ProjectFolder folder, string? sceneKey, TranslateDialogSettings current, IReadOnlyList<string>? chosenKeys = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(current);
        settings = current;
        var chosen = chosenKeys is { Count: > 1 } ? chosenKeys : null;
        scopes[0] = chosen is null
            ? sceneChoice
            : Hooked(new ScopeChoice(TranslationScope.Selection, $"As cenas escolhidas ({chosen.Count})"));
        byScope.Clear();
        LoadError = string.Empty;
        CanStart = false;
        StartCommand.NotifyCanExecuteChanged();
        IsOpen = true;
        IsLoading = true;
        Loading = LoadAsync(folder, sceneKey, chosen, ++version);
        return Loading;
    }

    private ScopeChoice Hooked(ScopeChoice choice)
    {
        choice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScopeChoice.IsSelected) && choice.IsSelected)
            {
                SelectedScope = choice;
            }
        };
        return choice;
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var request = new TranslationRequest(SelectedScope.Scope, SelectedQuality.Quality, byScope[SelectedScope.Scope].Keys);
        IsOpen = false;
        StartRequested?.Invoke(this, request);
    }

    [RelayCommand]
    private void SwitchToFit()
    {
        if (FitFor(SelectedScope.Scope) is { } fit)
        {
            SelectedQuality = Qualities.Single(q => q.Quality == fit.Quality);
        }
    }

    [RelayCommand]
    private void DefineGenders()
    {
        IsOpen = false;
        GlossaryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsOpen = false;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedScopeChanged(ScopeChoice value)
    {
        foreach (var scope in Scopes)
        {
            scope.IsSelected = scope == value;
        }

        Refresh();
    }

    partial void OnSelectedQualityChanged(QualityChoice value)
    {
        foreach (var quality in Qualities)
        {
            quality.IsSelected = quality == value;
        }

        Refresh();
    }

    private async Task LoadAsync(ProjectFolder folder, string? sceneKey, IReadOnlyList<string>? chosenKeys, int token)
    {
        var rate = settings.Rate;
        var shown = scopes.Select(s => s.Scope).ToList();
        ProjectRead loaded;
        try
        {
            loaded = await Task.Run(() => ReadProject(folder, sceneKey, chosenKeys, shown, rate)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            if (token == version)
            {
                IsLoading = false;
                LoadError = $"Não foi possível ler o projeto: {e.Message}";
                Refresh();
            }

            return;
        }

        if (token != version)
        {
            return;
        }

        byScope.Clear();
        foreach (var (scope, value) in loaded.Scopes)
        {
            byScope[scope] = (value.Keys, value.Quotes);
            var choice = Scopes.Single(s => s.Scope == scope);
            choice.Lines = value.Quotes.Lines;
            choice.IsAvailable = value.Quotes.Lines > 0;
            choice.Summary = Describe(scope, value.Quotes.Lines, value.Scenes);
        }

        spent = loaded.Spent;
        context = loaded.Context;
        IsLoading = false;
        SelectedScope = Scopes.FirstOrDefault(s => s.Lines > 0) ?? Scopes[0];
        SelectedQuality = Qualities.Single(q => q.Quality == settings.DefaultQuality);
        Refresh();
    }

    private sealed record ProjectRead(
        Dictionary<TranslationScope, (IReadOnlyList<string> Keys, TranslationQuotes Quotes, int Scenes)> Scopes,
        decimal Spent,
        TranslationContextSummary Context);

    private static ProjectRead ReadProject(ProjectFolder folder, string? sceneKey, IReadOnlyList<string>? chosenKeys, IEnumerable<TranslationScope> shown, ExchangeRate rate)
    {
        var texts = folder.LoadAllText();
        var config = folder.LoadConfig();
        var builder = new TranslationBatchBuilder(
            folder.LoadGlossary(),
            new CharacterBook(folder.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(folder.Root, TranslationContextSummary.StyleGuideFileName)),
            style: config.Style);
        var batchSize = Math.Clamp(config.Ai.BatchSize, TranslationRunOptions.MinBatchSize, TranslationRunOptions.MaxBatchSize);
        var effort = ClaudeEffortText.ParseOrDefault(config.Ai.Effort);
        var scopes = shown.ToDictionary(
            scope => scope,
            scope =>
            {
                var keys = TranslationQuoter.KeysFor(scope, texts.Keys, sceneKey, chosenKeys);
                var batches = TranslationQuoter.BatchesFor(texts, keys, builder, batchSize);
                var scenes = keys.Count(key => TranslationQuoter.PendingLines(texts, [key]).Count > 0);
                return (Keys: keys, Quotes: TranslationQuoter.Quote(batches, rate, effort), Scenes: scenes);
            });
        return new ProjectRead(scopes, rate.ToBrl(AiUsageLog.Load(folder).Dollars), TranslationContextSummary.Read(folder));
    }

    private static string Describe(TranslationScope scope, int lines, int scenes)
    {
        if (lines == 0)
        {
            return "Nada para traduzir aqui";
        }

        var text = lines == 1 ? "1 linha para traduzir" : $"{ProjectLabels.Number(lines)} linhas para traduzir";
        if (scope == TranslationScope.Scene)
        {
            return text;
        }

        var sceneWord = scenes == 1 ? "cena" : "cenas";
        return $"{text} em {scenes} {sceneWord}";
    }

    private QualityQuote? FitFor(TranslationScope scope) =>
        byScope.TryGetValue(scope, out var value) ? value.Quotes.MostCarefulThatFits(spent, settings.Limits.Limit) : null;

    private void Refresh()
    {
        if (!byScope.TryGetValue(SelectedScope.Scope, out var current))
        {
            CanStart = false;
            StartCommand.NotifyCanExecuteChanged();
            return;
        }

        RefreshQualities(current.Quotes);

        var request = current.Quotes.For(SelectedQuality.Quality).Brl;
        var limit = settings.Limits.Limit;
        Lines = current.Quotes.Lines;
        NothingToTranslate = Lines == 0;
        RequestBrl = request;
        AskBrl = (double)request;
        SpentBrl = (double)spent;
        LimitBrl = (double)(limit ?? 0m);
        RefreshAmounts(request, limit);

        IsOverLimit = !TranslationQuotes.Fits(request, spent, limit);
        var fit = IsOverLimit ? FitFor(SelectedScope.Scope) : null;
        CanSwitchToFit = fit is not null && fit.Quality != SelectedQuality.Quality;
        SwitchToFitText = fit is null ? string.Empty : $"Usar a qualidade {Qualities.Single(q => q.Quality == fit.Quality).Title} ({Money.BrlEstimate(fit.Brl)})";
        OverLimitText = IsOverLimit ? DescribeOverLimit(request, limit, fit) : string.Empty;
        AboveWarning = !IsOverLimit && settings.Limits.WarnAbove is { } warn && request > warn;
        RefreshBudgetProblem();
        RefreshNotices();

        CanStart = !IsLoading && Lines > 0 && !IsOverLimit && settings.KeyConfigured;
        StartCommand.NotifyCanExecuteChanged();
    }

    private void RefreshAmounts(decimal request, decimal? limit)
    {
        RequestText = $"Este pedido (aproximado): {Money.BrlEstimate(request)}";
        SpentText = limit is { } cap
            ? $"Já gasto no projeto: {Money.BrlEstimate(spent)} de {Money.Brl(cap)}"
            : $"Já gasto no projeto: {Money.BrlEstimate(spent)}";
        RateNote = settings.Rate.IsInformed ? string.Empty : "Cotação de exemplo: informe a sua em Ajustes para ver o valor certo.";
    }

    private void RefreshQualities(TranslationQuotes quotes)
    {
        foreach (var quality in Qualities)
        {
            var quote = quotes.For(quality.Quality);
            quality.Price = quote.Brl;
            quality.Duration = quotes.Lines == 0 ? "nenhuma linha para traduzir" : ProjectLabels.Duration(quote.Time);
        }
    }

    private void RefreshBudgetProblem()
    {
        HasBudgetProblem = settings.BudgetProblem is not null;
        BudgetProblemText = HasBudgetProblem
            ? "Não foi possível conferir o limite de gasto: o arquivo de Ajustes não pôde ser lido. Esta tradução não para num limite. Salve os Ajustes de novo para consertar, ou siga mesmo assim."
            : string.Empty;
        StartLabel = AboveWarning || HasBudgetProblem ? "Traduzir mesmo assim" : "Traduzir";
    }

    private void RefreshNotices()
    {
        KeyMissing = !settings.KeyConfigured;
        KeyMissingText = KeyMissing ? "Falta a chave da IA. Informe-a em Ajustes, na aba IA e gasto." : string.Empty;
        ContextText = DescribeContext(context);
        HasMissingGender = context.CharactersWithoutGender > 0;
        MissingGenderText = HasMissingGender
            ? $"{DescribeMissing(context.CharactersWithoutGender)}: a IA pode errar a concordância nas falas deles."
            : string.Empty;
    }

    private string DescribeOverLimit(decimal request, decimal? limit, QualityQuote? fit)
    {
        var over = request + spent - (limit ?? 0m);
        var head = $"Este pedido, pelo valor aproximado, passa do limite do projeto em {Money.BrlEstimate(over)}.";
        return fit is null
            ? $"{head} Aumente o limite em Ajustes ou escolha menos linhas."
            : $"{head} A qualidade {Qualities.Single(q => q.Quality == fit.Quality).Title} cabe no que sobra.";
    }

    private static string DescribeContext(TranslationContextSummary summary)
    {
        var parts = new List<string>();
        if (summary.GlossaryTerms > 0)
        {
            parts.Add(summary.GlossaryTerms == 1 ? "o glossário (1 termo)" : $"o glossário ({ProjectLabels.Number(summary.GlossaryTerms)} termos)");
        }

        if (summary.CharactersWithVoice > 0)
        {
            parts.Add("a voz dos personagens");
        }

        if (summary.HasStyleGuide)
        {
            parts.Add("o guia de estilo");
        }

        parts.Add("as falas em volta");
        var list = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} e {parts[^1]}";
        return $"Vai junto em cada pedido: {list}.";
    }

    private static string DescribeMissing(int count) =>
        count == 1 ? "1 personagem sem gênero" : $"{ProjectLabels.Number(count)} personagens sem gênero";
}
