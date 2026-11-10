using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>As três abas do jogo aberto: a ficha, a tradução (onde vive o trabalho de hoje) e jogar.</summary>
public enum GameTab
{
    Sheet,
    Translation,
    Play,
}

public sealed record GameTabItem(GameTab Tab, string Label);

/// <summary>O texto do cabeçalho do jogo sobre o andamento da tradução.</summary>
public static class GameProgress
{
    /// <summary>"Em tradução · 38%", "Traduzido · 100%" ou "Sem tradução ainda"; a porcentagem é a parte pronta, arredondada para baixo, como o Painel a escreve.</summary>
    public static string Describe(double ready, double total)
    {
        var percent = Percent(ready, total);
        if (percent >= 100)
        {
            return "Traduzido · 100%";
        }

        return percent == 0 ? "Sem tradução ainda" : $"Em tradução · {percent.ToString(CultureInfo.InvariantCulture)}%";
    }

    public static int Percent(double ready, double total) => total <= 0 ? 0 : (int)Math.Floor(100.0 * ready / total);
}

/// <summary>Página de uma aba do jogo que ainda não tem tela própria: uma frase e o caminho para o que existe.</summary>
public sealed class GameTabPlaceholderViewModel(string message, string actionText, Action action)
{
    public string Message { get; } = message;

    public string ActionText { get; } = actionText;

    public IRelayCommand ActionCommand { get; } = new RelayCommand(action);
}

/// <summary>O shell: Biblioteca no trilho, o jogo aberto com o seu cabeçalho e, abaixo dele, as abas Ficha, Tradução e Jogar.</summary>
public sealed partial class MainWindowViewModel
{
    private static readonly GameTabItem[] Tabs =
    [
        new(GameTab.Sheet, "Ficha"),
        new(GameTab.Translation, "Tradução"),
        new(GameTab.Play, "Jogar"),
    ];

    public IReadOnlyList<GameTabItem> GameTabs => Tabs;

    /// <summary>A segunda linha da aba Tradução, como no quadro: Painel, Editor, Revisão, Conferir, Testar, Glossário e "Mais", que abre o menu de <see cref="MoreSections"/>.</summary>
    public IReadOnlyList<NavItemViewModel> Sections { get; }

    /// <summary>O menu do "Mais": Lançar, Bíblia, Imagens (que não aparece no projeto de exemplo) e Provas, o banco de provas do Painel.</summary>
    public IReadOnlyList<NavItemViewModel> MoreSections { get; }

    /// <summary>O item "Mais" da segunda linha; a tela abre o menu em vez de trocar de página quando ele é escolhido.</summary>
    public NavItemViewModel MoreSection => moreItem;

    /// <summary>A aba Ficha do jogo (d9, #554).</summary>
    public GameSheetViewModel Sheet { get; }

    public GameTabPlaceholderViewModel Play { get; }

    /// <summary>Há um jogo aberto à vista: o cabeçalho dele e as abas aparecem acima da página.</summary>
    public bool InGame => HasProject && selectedNav is { } item && GameTabOf(item.Key) is not null;

    /// <summary>
    /// O modo revisão ocupa a página inteira, com o seu próprio "Voltar ao editor": enquanto ele está aberto o cabeçalho do jogo sai, para o palco e o roteiro
    /// ficarem com a altura que sempre tiveram.
    /// </summary>
    public bool ShowGameHeader => InGame && !(selectedNav is { } item && ReferenceEquals(item, editorItem) && Editor.IsReviewing);

    /// <summary>Na Ficha e em Jogar o botão principal do cabeçalho completo é "Jogar" (que só existe com a #549); na Tradução é o próximo passo do Painel.</summary>
    public bool ShowPlayButton => InGame && SelectedGameTab?.Tab != GameTab.Translation && !(SelectedGameTab?.Tab == GameTab.Sheet && Sheet.IsReviewing);

    /// <summary>A segunda linha só existe na aba Tradução.</summary>
    public bool ShowSections => InGame && SelectedGameTab?.Tab == GameTab.Translation;

    /// <summary>"Biblioteca › Amaoto no Hakoniwa" dentro do jogo e nos Ajustes de um projeto aberto; "Ajustes" nos Ajustes sem projeto; "Biblioteca" no resto.</summary>
    public string Breadcrumb => InGame && SelectedGameTab?.Tab == GameTab.Sheet && Sheet.IsReviewing ? $"Biblioteca › {ProjectTitle} › Revisar a ficha" : InGame || (HasProject && selectedNav?.Key == NavKey.Settings) ? $"Biblioteca › {ProjectTitle}" : selectedNav?.Key == NavKey.Settings ? "Ajustes" : "Biblioteca";

    /// <summary>Atraso entre gravar uma linha e reler o andamento do jogo para o cabeçalho; várias gravações seguidas fazem uma leitura só.</summary>
    public TimeSpan ProgressRefreshDelay { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>A última releitura do Painel que o cabeçalho pediu (ao entrar no jogo ou depois de gravar linhas); os testes a aguardam.</summary>
    public Task DashboardRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>Há um jogo aberto antes e a página de agora é a Biblioteca ou os Ajustes: o botão da barra de título volta a ele.</summary>
    public bool CanReturnToGame => HasProject && lastGameItem is not null && selectedNav?.Key is NavKey.Start or NavKey.Settings;

    /// <summary>Verde quando o jogo está todo pronto, neutro quando nada foi feito, azul no meio.</summary>
    public bool GameProgressDone => GameProgress.Percent(ReadyLines, TotalLines) >= 100;

    public bool GameProgressNone => GameProgress.Percent(ReadyLines, TotalLines) == 0;

    public string GameEngineLabel => CurrentProject is { } project ? ProjectLabels.EngineName(project.EngineId) : string.Empty;

    /// <summary>A parte pronta do jogo, de 0 a 100, para a barra da capa; vem do Painel e, antes de ele ler a pasta, do resumo do projeto.</summary>
    public double GameProgressPercent => GameProgress.Percent(ReadyLines, TotalLines);

    public string GameProgressLabel => GameProgress.Describe(ReadyLines, TotalLines);

    private double ReadyLines => Dashboard.TotalLines > 0 ? Dashboard.ReadyLines : CurrentProject?.Counts.Ready ?? 0;

    private double TotalLines => Dashboard.TotalLines > 0 ? Dashboard.TotalLines : CurrentProject?.Counts.Total ?? 0;

    public GameTabItem? SelectedGameTab
    {
        get => InGame && selectedNav is { } item ? Tabs.Single(t => t.Tab == GameTabOf(item.Key)) : null;
        set
        {
            if (value is { Tab: GameTab.Sheet } && Sheet.IsReviewing)
            {
                // A aba Ficha já está marcada durante a revisão: clicar nela volta para a ficha.
                Sheet.ExitReviewCommand.Execute(null);
            }

            if (value is not null && HasProject)
            {
                SelectedNav = value.Tab switch
                {
                    GameTab.Sheet => sheetItem,
                    GameTab.Play => playItem,
                    _ => lastTranslationItem,
                };
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// A seção marcada da segunda linha. O Editor e a Revisão são a mesma página: a Revisão é a marcada enquanto o modo revisão está aberto
    /// e escolher o Editor o fecha. Lançar, Bíblia, Imagens e as Provas do Painel marcam o "Mais".
    /// </summary>
    public NavItemViewModel? SelectedSection
    {
        get
        {
            if (!ShowSections || selectedNav is not { } item)
            {
                return null;
            }

            if (item.Key is NavKey.Release or NavKey.Bible or NavKey.Images || (item.Key == NavKey.Dashboard && Dashboard.SelectedTab == 1))
            {
                return moreItem;
            }

            return ReferenceEquals(item, editorItem) && Editor.IsReviewing ? reviewItem : item;
        }
        set
        {
            // "Mais" não é página: a tela abre o menu, e a marca volta para a seção que está aberta.
            if (value is not null && !ReferenceEquals(value, moreItem))
            {
                OpenSection(value);
            }

            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void OpenLibrary() => SelectedNav = Nav[0];

    [RelayCommand]
    private void ReturnToGame()
    {
        if (lastGameItem is { } item)
        {
            SelectedNav = item;
        }
    }

    /// <summary>Gravar linhas muda o andamento e o próximo passo: relê o Painel depois de um instante, sem reler a cada tecla.</summary>
    private void ScheduleProgressRefresh()
    {
        if (CurrentProject is null)
        {
            return;
        }

        // A releitura anterior só lê o token, o que continua valendo depois do Dispose porque o Cancel vem antes.
        var old = progressRefresh;
        old?.Cancel();
        old?.Dispose();
        var source = progressRefresh = new CancellationTokenSource();
        DashboardRefresh = RefreshProgressAsync(source.Token);
    }

    private async Task RefreshProgressAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ProgressRefreshDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Ninguém aguarda esta tarefa fora dos testes: uma falha na releitura aparece como aviso em vez de sumir com ela.
        try
        {
            await Dashboard.ReloadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            Toasts.Show("Não foi possível atualizar o andamento do jogo. Ele volta a ser lido ao abrir o Painel.", ToastKind.Warning, key: "progress-refresh");
        }
    }

    private void RaiseProgressChanged()
    {
        OnPropertyChanged(nameof(GameProgressPercent));
        OnPropertyChanged(nameof(GameProgressLabel));
        OnPropertyChanged(nameof(GameProgressDone));
        OnPropertyChanged(nameof(GameProgressNone));
    }

    [RelayCommand]
    private void OpenSection(NavItemViewModel section)
    {
        if (ReferenceEquals(section, reviewItem))
        {
            if (reviewItem.IsEnabled && GoTo(editorItem))
            {
                Editor.StartReview();
            }

            return;
        }

        if (ReferenceEquals(section, editorItem) && Editor.IsReviewing)
        {
            Editor.StopReview();
        }

        // O banco de provas é a segunda aba do Painel: "Provas" abre o Painel nela e "Painel" volta para a visão geral.
        var proofs = ReferenceEquals(section, proofsItem);
        SelectedNav = proofs ? dashboardItem : section;
        if (ReferenceEquals(selectedNav, dashboardItem) && (proofs || ReferenceEquals(section, dashboardItem)))
        {
            Dashboard.SelectedTab = proofs ? 1 : 0;
        }

        OnPropertyChanged(nameof(SelectedSection));
    }

    private static GameTab? GameTabOf(NavKey key) => key switch
    {
        NavKey.Sheet => GameTab.Sheet,
        NavKey.Play => GameTab.Play,
        NavKey.Dashboard or NavKey.Editor or NavKey.Conference or NavKey.Build or NavKey.Release or NavKey.Glossary or NavKey.Bible or NavKey.Images => GameTab.Translation,
        _ => null,
    };

    /// <summary>Tudo o que o shell mostra depende da página aberta: o trilho, o cabeçalho, as abas e a segunda linha.</summary>
    private void RaiseShellChanged()
    {
        OnPropertyChanged(nameof(SelectedTopNav));
        OnPropertyChanged(nameof(SelectedBottomNav));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(ShowGameHeader));
        OnPropertyChanged(nameof(ShowSections));
        OnPropertyChanged(nameof(ShowPlayButton));
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(SelectedGameTab));
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(CanReturnToGame));
    }
}
