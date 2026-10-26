using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Os cinco passos da jornada que o Painel mostra, na ordem em que a pessoa os cumpre.</summary>
public enum JourneyStep
{
    Read,
    Translate,
    Review,
    Check,
    Test,
}

/// <summary>
/// O que o Painel pergunta ao app além do que está na pasta do projeto: a cotação e o limite de gasto dos Ajustes e se há chave do Claude.
/// O padrão (sem Ajustes) tem a cotação de exemplo, nenhum limite e a chave presente, para não acusar uma falta que ninguém checou.
/// </summary>
public sealed record DashboardContext(ExchangeRate Rate, BudgetLimits Limits, bool HasApiKey)
{
    public static DashboardContext Default { get; } = new(ExchangeRate.Placeholder, BudgetLimits.None, true);
}

/// <summary>Uma linha do sumário de capítulos: o grupo de scripts com os três totais e a nota. Clicar nela abre o editor na primeira cena que pede trabalho.</summary>
public sealed class ChapterRowViewModel(ScriptGroupTotals group, string note, bool isHere, string? scriptKey)
{
    public string Name { get; } = group.Name;

    /// <summary>A posição na história; nula em "Menus e nomes".</summary>
    public int? Number { get; } = group.Number;

    public bool IsExtra { get; } = group.IsExtra;

    public bool IsHere { get; } = isHere;

    public double Ready { get; } = group.Ready;

    public double ToReview { get; } = group.ToReview;

    public double Total { get; } = group.Total;

    public string Note { get; } = note;

    /// <summary>A primeira cena do capítulo com linhas para revisar, senão com linhas em japonês, senão a primeira.</summary>
    public string? ScriptKey { get; } = scriptKey;
}

/// <summary>Um item de "Precisa de você": a gravidade, o que falta, o porquê e o botão que leva à tela que resolve.</summary>
public sealed class TodoRowViewModel(SeverityLevel severity, string title, string message, string? actionText = null, ICommand? command = null)
{
    public SeverityLevel Severity { get; } = severity;

    public string Title { get; } = title;

    public string Message { get; } = message;

    public string? ActionText { get; } = actionText;

    public ICommand? Command { get; } = command;
}

/// <summary>Para onde um botão do Painel leva: a tela e, no Editor, o script a abrir.</summary>
public sealed record DashboardNavigation(NavKey Target, string? ScriptKey = null);

/// <summary>
/// Página Painel: onde o projeto está e o que fazer agora. Um botão com o próximo passo, a jornada em cinco passos, o mapa do jogo (anel e sumário de capítulos
/// com a frase das horas de revisão), a lista "Precisa de você", o gasto com IA em reais e o estado do jogo. Os números vêm de <see cref="ProjectDashboard"/>,
/// a mesma contagem do <c>vnf status</c> e do <c>vnf check</c>. Só lê; a página recarrega ao ser aberta.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    /// <summary>Quantos itens "Precisa de você" cabem na lista; o resto some até os de cima serem resolvidos.</summary>
    public const int MaxTodos = 4;

    private readonly Func<string, ProjectDashboard> loader;
    private readonly TimeProvider clock;
    private int loadVersion;
    private bool loaded;
    private DashboardNavigation? nextStep;

    /// <param name="loader">Lê o painel de uma pasta de projeto; o padrão lê o disco.</param>
    /// <param name="clock">O relógio do ritmo de revisão (o "hoje" e a janela de sete dias); o padrão é o do sistema.</param>
    public DashboardViewModel(Func<string, ProjectDashboard>? loader = null, TimeProvider? clock = null)
    {
        this.loader = loader ?? (root => ProjectDashboard.Read(ProjectFolder.Open(root)));
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>Pedido para ir ao Início (a ação do estado vazio "sem projeto").</summary>
    public event EventHandler? StartRequested;

    /// <summary>Pedido para ir a uma tela (Editor, Conferência, Testar, Ajustes ou Glossário), de um botão ou link do Painel.</summary>
    public event EventHandler<DashboardNavigation>? NavigationRequested;

    /// <summary>O que o Painel pergunta ao app (cotação, limite, chave); a janela principal o liga aos Ajustes.</summary>
    public Func<DashboardContext> Context { get; set; } = () => DashboardContext.Default;

    /// <summary>Os cinco passos como o <see cref="PipelineStepper"/> os recebe: número, nome, frase, estado e o comando que abre a tela.</summary>
    public ObservableCollection<PipelineStep> Steps { get; } = [];

    /// <summary>As linhas do sumário: um capítulo por linha na ordem da história, "Menus e nomes" por último.</summary>
    public ObservableCollection<ChapterRowViewModel> Chapters { get; } = [];

    /// <summary>"Precisa de você": até <see cref="MaxTodos"/> itens, ou o aviso de que está tudo em ordem.</summary>
    public ObservableCollection<TodoRowViewModel> Todos { get; } = [];

    /// <summary>A legenda do anel: pronto, esperando revisão e ainda em japonês, com as linhas de cada um.</summary>
    public ObservableCollection<LegendEntry> MapLegend { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>O texto do botão do próximo passo ("Revisar o capítulo 1"); vazio sem passo a dar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextStep))]
    public partial string NextStepLabel { get; private set; } = string.Empty;

    /// <summary>As linhas prontas, esperando revisão e o total: o que o anel e a barra do mapa desenham.</summary>
    [ObservableProperty]
    public partial double ReadyLines { get; private set; }

    [ObservableProperty]
    public partial double ReviewLines { get; private set; }

    [ObservableProperty]
    public partial double TotalLines { get; private set; }

    /// <summary>"18.592 de 48.211 linhas prontas".</summary>
    [ObservableProperty]
    public partial string MapSummary { get; private set; } = string.Empty;

    /// <summary>A frase com as horas de revisão que faltam no ritmo da pessoa.</summary>
    [ObservableProperty]
    public partial string PaceText { get; private set; } = string.Empty;

    /// <summary>"Mais 2 coisas para ver depois", quando a lista tem mais itens do que cabem.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreTodos))]
    public partial string MoreTodosText { get; private set; } = string.Empty;

    /// <summary>"R$ 142,56 de R$ 300,00", ou só o gasto quando não há limite.</summary>
    [ObservableProperty]
    public partial string SpentText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double SpentBrl { get; private set; }

    [ObservableProperty]
    public partial double LimitBrl { get; private set; }

    /// <summary>Uma frase sobre o limite: o quanto falta ou o convite para definir um.</summary>
    [ObservableProperty]
    public partial string SpendHint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial GameState GameState { get; private set; }

    [ObservableProperty]
    public partial string GameTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameMessage { get; private set; } = string.Empty;

    /// <summary>"Testar de novo", ou "Testar no jogo" enquanto nenhum teste foi feito.</summary>
    [ObservableProperty]
    public partial string GameActionText { get; private set; } = TestInGame;

    public bool HasProject => Project is not null;

    public bool HasNextStep => nextStep is not null;

    public bool HasMoreTodos => MoreTodosText.Length > 0;

    /// <summary>Lendo os números: o sumário mostra o esqueleto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(EmptyMessage), nameof(ShowChaptersSkeleton))]
    public partial bool IsLoading { get; private set; }

    /// <summary>O esqueleto só entra quando não há linhas antigas para mostrar; ao reler, as anteriores ficam até as novas chegarem.</summary>
    public bool ShowChaptersSkeleton => IsLoading && Chapters.Count == 0;

    /// <summary>Sem projeto aberto, ou projeto lido sem nenhuma linha: o Painel mostra o estado vazio com o selo 訳.</summary>
    public bool ShowEmptyState => Project is null || (loaded && !IsLoading && ErrorMessage is null && TotalLines == 0);

    public string EmptyMessage => Project is null
        ? "Nenhum projeto aberto. Abra um no Início para ver o painel."
        : "Este projeto ainda não tem texto lido. Leia o texto do jogo para ver o progresso aqui.";

    /// <summary>A ação do estado vazio: só existe quando falta o projeto.</summary>
    public string? EmptyActionText => Project is null ? "Abrir projeto" : null;

    /// <summary>O nome do projeto, o título da página.</summary>
    public string Title => Project?.Name ?? string.Empty;

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
        IsLoading = true;
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

        try
        {
            Apply(project, dashboard, Context());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Clear();
            ErrorMessage = $"Não foi possível ler o projeto: {ex.Message}";
            return;
        }

        loaded = true;
        IsLoading = false;
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    [RelayCommand]
    private void OpenStart() => StartRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenNextStep()
    {
        if (nextStep is { } step)
        {
            NavigationRequested?.Invoke(this, step);
        }
    }

    [RelayCommand]
    private void OpenStep(JourneyStep step)
    {
        if (StepTarget(step) is { } target)
        {
            NavigationRequested?.Invoke(this, target);
        }
    }

    [RelayCommand]
    private void OpenChapter(ChapterRowViewModel? chapter)
    {
        if (chapter is not null)
        {
            NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Editor, chapter.ScriptKey));
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

    [RelayCommand]
    private void OpenTest() => NavigationRequested?.Invoke(this, new DashboardNavigation(NavKey.Build));

    /// <summary>Ao trocar de projeto os números do anterior saem na hora, e uma leitura ainda em curso deixa de valer.</summary>
    partial void OnProjectChanged(ProjectSummary? value)
    {
        loadVersion++;
        Clear();
    }

    private const string TestInGame = "Testar no jogo";

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{ProjectLabels.Number(count)} {many}";

    private static string Lines(int count) => Plural(count, "linha", "linhas");

    private static string ReviewPhrase(StatusCounts total, bool reviewed)
    {
        if (reviewed)
        {
            return "Tudo revisado";
        }

        if (total.ToReview == 0)
        {
            return "Espera a tradução";
        }

        return total.ToReview == 1 ? "1 linha espera você" : $"{ProjectLabels.Number(total.ToReview)} linhas esperam você";
    }

    private static string MoreText(int more)
    {
        if (more <= 0)
        {
            return string.Empty;
        }

        return more == 1 ? "Mais 1 coisa para ver depois" : $"Mais {more} coisas para ver depois";
    }

    private static string SpendHintFor(decimal? limit, decimal spent)
    {
        if (limit is not { } max)
        {
            return "Sem limite de gasto. Defina um nos Ajustes.";
        }

        return spent >= max ? "O limite foi atingido." : $"Faltam {Money.Brl(max - spent)} para o limite.";
    }

    private static string ErrorNote(int errors, bool isHere, string? scene)
    {
        if (errors != 1)
        {
            return $"{ProjectLabels.Number(errors)} erros travam o teste";
        }

        if (!isHere)
        {
            return "1 erro trava o teste";
        }

        return $"1 erro {(scene is null ? "numa cena" : $"em {scene}")}";
    }

    private static DashboardNavigation? StepTarget(JourneyStep step) => step switch
    {
        JourneyStep.Translate or JourneyStep.Review => new DashboardNavigation(NavKey.Editor),
        JourneyStep.Check => new DashboardNavigation(NavKey.Conference),
        JourneyStep.Test => new DashboardNavigation(NavKey.Build),
        _ => null,
    };

    private void Clear()
    {
        loaded = false;
        IsLoading = false;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(EmptyActionText));
        Steps.Clear();
        Chapters.Clear();
        Todos.Clear();
        MapLegend.Clear();
        ErrorMessage = null;
        nextStep = null;
        NextStepLabel = string.Empty;
        ReadyLines = ReviewLines = TotalLines = 0;
        MapSummary = PaceText = MoreTodosText = SpentText = SpendHint = GameTitle = GameMessage = string.Empty;
        SpentBrl = LimitBrl = 0;
        GameState = GameState.Original;
        GameActionText = TestInGame;
        OnPropertyChanged(nameof(HasNextStep));
    }

    private void Apply(ProjectSummary project, ProjectDashboard dashboard, DashboardContext context)
    {
        var total = dashboard.Status.Total;
        var chapters = dashboard.Chapters();
        var spent = context.Rate.ToBrl(dashboard.AiUsage.Dollars);
        var limitReached = context.Limits.Limit is { } limit && limit > 0m && spent >= limit;
        var pace = ReviewPace.Compute(dashboard.Sessions, total.ToReview, clock.GetUtcNow(), clock.LocalTimeZone);

        ErrorMessage = null;
        ReadyLines = total.Ready;
        ReviewLines = total.ToReview;
        TotalLines = total.Total;
        MapSummary = $"{ProjectLabels.Number(total.Ready)} de {Lines(total.Total)} prontas";
        PaceText = PaceSentence(pace, total.ToReview);
        MapLegend.Clear();
        MapLegend.Add(new LegendEntry($"Pronto {ProjectLabels.Number(total.Ready)}", LegendTone.Approved));
        MapLegend.Add(new LegendEntry($"Esperando revisão {ProjectLabels.Number(total.ToReview)}", LegendTone.Machine));
        MapLegend.Add(new LegendEntry($"Ainda em japonês {ProjectLabels.Number(total.Untranslated)}", LegendTone.Pending));

        ApplySteps(dashboard);
        ApplyChapters(project, dashboard, chapters, limitReached);
        ApplyTodos(dashboard, context, limitReached);
        ApplySpend(context, spent);
        ApplyGame(dashboard);
        ApplyNextStep(dashboard, chapters);
    }

    private static string PaceSentence(ReviewPace pace, int toReview)
    {
        var today = pace.Today > 0 ? $" Hoje você aprovou {Lines(pace.Today)}." : string.Empty;
        if (toReview == 0)
        {
            return "Nenhuma linha espera revisão agora." + today;
        }

        if (pace.PerHour is not { } perHour || pace.HoursLeft is not { } hours)
        {
            return "Aprove algumas linhas e o app calcula quantas horas de revisão faltam." + today;
        }

        var left = hours <= 1 ? "cerca de 1 hora" : $"cerca de {ProjectLabels.Number(hours)} horas";
        return $"No seu ritmo ({Lines(perHour)} por hora), faltam {left} de revisão.{today}";
    }

    private void ApplySteps(ProjectDashboard dashboard)
    {
        var total = dashboard.Status.Total;
        var stage = dashboard.Stages.ToDictionary(s => s.Key);
        var translated = stage[PipelineStageKey.Translate].IsComplete;
        var reviewed = stage[PipelineStageKey.Review].IsComplete;
        var checkedOk = dashboard.Qa is { Total: 0 } && translated;
        var tested = IsTested(dashboard);
        bool[] complete = [stage[PipelineStageKey.Extract].IsComplete, translated, reviewed, checkedOk, tested];
        string[] titles = ["Ler o texto", "Traduzir com a IA", "Revisar", "Conferir", TestInGame];
        string[] phrases =
        [
            stage[PipelineStageKey.Extract].IsComplete
                ? $"{Lines(total.Total)} em {Plural(dashboard.Status.Files.Count, "cena", "cenas")}"
                : "O texto do jogo ainda não foi lido",
            translated ? "Tudo traduzido" : $"{Math.Floor(total.Percent)}% traduzido · faltam {Lines(total.Untranslated)}",
            ReviewPhrase(total, reviewed),
            dashboard.Qa switch
            {
                null => "A conferência não pôde rodar",
                { Total: 0 } => "Nenhum problema",
                var qa => $"{Plural(qa.Errors, "erro", "erros")} · {Plural(qa.Warnings, "aviso", "avisos")}",
            },
            TestPhrase(dashboard, tested),
        ];
        var states = PipelineStepper.Resolve(complete);
        Steps.Clear();
        for (var i = 0; i < complete.Length; i++)
        {
            var step = (JourneyStep)i;
            Steps.Add(new PipelineStep(i + 1, titles[i], phrases[i], states[i], StepTarget(step) is null ? null : OpenStepCommand, step));
        }
    }

    /// <summary>O jogo já tem uma tradução do projeto (<c>game-state.json</c>, o que o cartão do jogo também lê) ou o build está em dia e injetado.</summary>
    private static bool IsTested(ProjectDashboard dashboard) =>
        dashboard.GameState.InGame is not null
        || dashboard.Stages.Where(s => s.Key is PipelineStageKey.Build or PipelineStageKey.Inject).All(s => s.IsComplete);

    private static string TestPhrase(ProjectDashboard dashboard, bool tested)
    {
        if (tested && dashboard.GameState.InGame is { } inGame)
        {
            return DescribeGame(inGame);
        }

        return dashboard.Build == BuildStatus.Stale ? "Os textos mudaram desde o último teste" : "Ainda não testado no jogo";
    }

    private void ApplyChapters(ProjectSummary project, ProjectDashboard dashboard, IReadOnlyList<ScriptGroupTotals> chapters, bool limitReached)
    {
        var counts = dashboard.Status.Files.ToDictionary(f => f.Key, f => f.Counts, StringComparer.Ordinal);
        var stopped = limitReached ? chapters.FirstOrDefault(c => !c.IsExtra && c.Japanese > 0) : null;
        Chapters.Clear();
        foreach (var chapter in chapters)
        {
            var key = chapter.ScriptKeys.FirstOrDefault(k => counts[k].ToReview > 0)
                ?? chapter.ScriptKeys.FirstOrDefault(k => counts[k].Untranslated > 0)
                ?? chapter.ScriptKeys[0];
            var isHere = project.LastEditedScript is { } last && chapter.ScriptKeys.Contains(last, StringComparer.Ordinal);
            var note = ChapterNote(chapter, dashboard, isHere, ReferenceEquals(chapter, stopped));
            Chapters.Add(new ChapterRowViewModel(chapter, note, isHere, key));
        }

        OnPropertyChanged(nameof(ShowChaptersSkeleton));
    }

    private static string ChapterNote(ScriptGroupTotals chapter, ProjectDashboard dashboard, bool isHere, bool aiStopped)
    {
        var notes = new List<string>();
        if (isHere)
        {
            notes.Add("Você está aqui");
        }

        var withProblems = chapter.ScriptKeys.Where(dashboard.QaByScript.ContainsKey).ToList();
        var errors = withProblems.Sum(k => dashboard.QaByScript[k].Errors);
        var warnings = withProblems.Sum(k => dashboard.QaByScript[k].Warnings);
        if (errors > 0)
        {
            var scene = dashboard.ScriptNames.DisplayName(withProblems.First(k => dashboard.QaByScript[k].Errors > 0));
            notes.Add(ErrorNote(errors, isHere, scene));
        }
        else if (warnings > 0)
        {
            notes.Add(Plural(warnings, "aviso", "avisos"));
        }

        if (aiStopped)
        {
            notes.Add("A IA parou no limite de gasto");
        }

        return string.Join(" · ", notes);
    }

    private void ApplyTodos(ProjectDashboard dashboard, DashboardContext context, bool limitReached)
    {
        var facts = dashboard.Readiness with { HasApiKey = context.HasApiKey, SpendLimitReached = limitReached };
        var items = ProjectReadiness.Evaluate(facts);
        Todos.Clear();
        foreach (var item in items.Take(MaxTodos))
        {
            Todos.Add(Describe(item));
        }

        if (Todos.Count == 0)
        {
            Todos.Add(new TodoRowViewModel(SeverityLevel.Ok, "Tudo em ordem", "Nada trava a tradução agora."));
        }

        var more = items.Count - MaxTodos;
        MoreTodosText = MoreText(more);
    }

    private TodoRowViewModel Describe(ReadinessItem item)
    {
        var severity = item.Severity == ReadinessSeverity.Error ? SeverityLevel.Error : SeverityLevel.Warning;
        return item.Kind switch
        {
            ReadinessKind.ApiKey => new(severity, "Falta a chave do Claude", "Sem ela a IA não traduz. A chave fica guardada no Windows.", "Colocar a chave", Go(NavKey.Settings)),
            ReadinessKind.QaErrors => new(
                severity,
                item.Count == 1 ? "1 erro trava o teste no jogo" : $"{ProjectLabels.Number(item.Count)} erros travam o teste no jogo",
                "Os erros precisam ser corrigidos antes de abrir o jogo.",
                "Corrigir",
                Go(NavKey.Conference)),
            ReadinessKind.SpendLimit => new(severity, "A IA parou no limite de gasto", "Aumente o limite nos Ajustes para a IA continuar traduzindo.", "Ver o gasto", Go(NavKey.Settings)),
            ReadinessKind.CharacterGender => new(
                severity,
                item.Count == 1 ? "1 personagem sem gênero" : $"{ProjectLabels.Number(item.Count)} personagens sem gênero",
                "A IA precisa do gênero para concordar o texto.",
                "Definir agora",
                Go(NavKey.Glossary)),
            ReadinessKind.AccentTest => new(severity, "Teste os acentos no jogo", "Veja se ç, ã, é e ô aparecem certos antes de traduzir tudo.", "Testar acentos", Go(NavKey.Build)),
            ReadinessKind.Narrator => new(severity, "Diga quem narra", "Isso decide a concordância das falas que não são de um personagem.", "Escolher", Go(NavKey.Settings)),
            ReadinessKind.StyleGuide => new(severity, "Escreva um guia de estilo", "Poucas frases sobre o tom ajudam a IA a manter a voz.", "Escrever", Go(NavKey.Settings)),
            _ => new(
                severity,
                item.Count == 1 ? "1 aviso para olhar" : $"{ProjectLabels.Number(item.Count)} avisos para olhar",
                "Não travam o teste, mas podem deixar uma fala estranha.",
                "Ver os avisos",
                Go(NavKey.Conference)),
        };
    }

    private RelayCommand Go(NavKey target) => new(() => NavigationRequested?.Invoke(this, new DashboardNavigation(target)));

    private void ApplySpend(DashboardContext context, decimal spent)
    {
        var limit = context.Limits.Limit is { } value && value > 0m ? value : (decimal?)null;
        SpentBrl = (double)spent;
        LimitBrl = (double)(limit ?? 0m);
        var text = limit is { } cap ? $"{Money.BrlEstimate(spent)} de {Money.Brl(cap)}" : $"{Money.BrlEstimate(spent)} neste projeto";
        SpentText = Money.WithRateMark(text, spent, context.Rate);
        SpendHint = SpendHintFor(limit, spent);
    }

    private void ApplyGame(ProjectDashboard dashboard)
    {
        var inGame = dashboard.GameState.InGame;
        GameActionText = dashboard.GameState.LastTest is not null || inGame is not null ? "Testar de novo" : TestInGame;
        if (inGame is { BackupDirectory: not null } && dashboard.LastBackupAt is null)
        {
            GameState = GameState.Warning;
            GameTitle = "O jogo está com a tradução, mas a cópia de segurança não foi encontrada";
            GameMessage = "Sem ela o app não consegue devolver o jogo ao original.";
        }
        else
        {
            GameState = inGame is null ? GameState.Original : GameState.Translated;
            GameTitle = DescribeGame(inGame);
            GameMessage = inGame is null ? "Nenhuma tradução foi copiada para o jogo." : "A tradução continua no projeto.";
        }
    }

    private static string DescribeGame(GameInGame? inGame) => inGame switch
    {
        null => "O jogo está como veio",
        { Kind: GameTestKind.AccentTest } => $"O jogo está com o teste de acentos de {DateText(inGame.AppliedAt)}",
        { ScriptKey: not null } => $"O jogo está com só uma cena traduzida em {DateText(inGame.AppliedAt)}",
        _ => $"O jogo está com a tradução de {DateText(inGame.AppliedAt)}",
    };

    private static string DateText(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// O botão de cima: revisar o primeiro capítulo com linhas da IA, senão traduzir o primeiro com japonês, senão conferir, senão testar no jogo.
    /// Sem linhas nenhum passo existe.
    /// </summary>
    private void ApplyNextStep(ProjectDashboard dashboard, IReadOnlyList<ScriptGroupTotals> chapters)
    {
        var total = dashboard.Status.Total;
        var tested = IsTested(dashboard);
        if (total.Total == 0)
        {
            nextStep = null;
            NextStepLabel = string.Empty;
        }
        else if (ChapterToWorkOn(chapters, c => c.ToReview > 0) is { } review)
        {
            SetNextStep($"Revisar {Phrase(review)}", ChapterRowFor(review));
        }
        else if (ChapterToWorkOn(chapters, c => c.Japanese > 0) is { } pending)
        {
            SetNextStep($"Traduzir {Phrase(pending)}", ChapterRowFor(pending));
        }
        else if (dashboard.Qa is { Total: > 0 })
        {
            SetNextStep("Conferir a tradução", new DashboardNavigation(NavKey.Conference));
        }
        else
        {
            SetNextStep(tested ? "Testar de novo no jogo" : TestInGame, new DashboardNavigation(NavKey.Build));
        }

        OnPropertyChanged(nameof(HasNextStep));

        DashboardNavigation ChapterRowFor(ScriptGroupTotals chapter) =>
            new(NavKey.Editor, Chapters.FirstOrDefault(c => c.Name == chapter.Name)?.ScriptKey);
    }

    /// <summary>O capítulo onde a pessoa está, se ainda tem o que fazer nele; senão o primeiro da história que tem. O botão e o "Você está aqui" apontam o mesmo capítulo sempre que possível.</summary>
    private ScriptGroupTotals? ChapterToWorkOn(IReadOnlyList<ScriptGroupTotals> chapters, Func<ScriptGroupTotals, bool> needsWork)
    {
        var here = Chapters.FirstOrDefault(c => c.IsHere)?.Name;
        return chapters.FirstOrDefault(c => c.Name == here && needsWork(c)) ?? chapters.FirstOrDefault(needsWork);
    }

    private void SetNextStep(string label, DashboardNavigation target)
    {
        nextStep = target;
        NextStepLabel = label;
    }

    /// <summary>"o capítulo 1", "a parte 2", "“Manhã de chuva”" ou "menus e nomes": o capítulo como a frase do botão o chama.</summary>
    private static string Phrase(ScriptGroupTotals chapter) => chapter.Name switch
    {
        _ when chapter.IsExtra => "menus e nomes",
        var name when name.StartsWith("Capítulo ", StringComparison.Ordinal) => $"o capítulo {name["Capítulo ".Length..]}",
        var name when name.StartsWith("Parte ", StringComparison.Ordinal) => $"a parte {name["Parte ".Length..]}",
        var name => $"“{name}”",
    };
}
