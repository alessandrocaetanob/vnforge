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

    /// <summary>A segunda linha da aba Tradução: Painel, Editor, Revisão, Conferir, Testar e Glossário.</summary>
    public IReadOnlyList<NavItemViewModel> Sections { get; }

    public GameTabPlaceholderViewModel Sheet { get; }

    public GameTabPlaceholderViewModel Play { get; }

    /// <summary>Há um jogo aberto à vista: o cabeçalho dele e as abas aparecem acima da página.</summary>
    public bool InGame => HasProject && selectedNav is { } item && GameTabOf(item.Key) is not null;

    /// <summary>A segunda linha só existe na aba Tradução.</summary>
    public bool ShowSections => InGame && SelectedGameTab?.Tab == GameTab.Translation;

    /// <summary>"Biblioteca › Amaoto no Hakoniwa" dentro do jogo e nos Ajustes de um projeto aberto; "Ajustes" nos Ajustes sem projeto; "Biblioteca" no resto.</summary>
    public string Breadcrumb => InGame || (HasProject && selectedNav?.Key == NavKey.Settings) ? $"Biblioteca › {ProjectTitle}" : selectedNav?.Key == NavKey.Settings ? "Ajustes" : "Biblioteca";

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
    /// e escolher o Editor o fecha.
    /// </summary>
    public NavItemViewModel? SelectedSection
    {
        get
        {
            if (!ShowSections || selectedNav is not { } item)
            {
                return null;
            }

            return ReferenceEquals(item, editorItem) && Editor.IsReviewing ? reviewItem : item;
        }
        set
        {
            if (value is not null)
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

        progressRefresh?.Cancel();
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

        await Dashboard.ReloadAsync();
    }

    private void RaiseProgressChanged()
    {
        OnPropertyChanged(nameof(GameProgressPercent));
        OnPropertyChanged(nameof(GameProgressLabel));
        OnPropertyChanged(nameof(GameProgressDone));
        OnPropertyChanged(nameof(GameProgressNone));
    }

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

        SelectedNav = section;
    }

    private static GameTab? GameTabOf(NavKey key) => key switch
    {
        NavKey.Sheet => GameTab.Sheet,
        NavKey.Play => GameTab.Play,
        NavKey.Dashboard or NavKey.Editor or NavKey.Conference or NavKey.Build or NavKey.Glossary => GameTab.Translation,
        _ => null,
    };

    /// <summary>Tudo o que o shell mostra depende da página aberta: o trilho, o cabeçalho, as abas e a segunda linha.</summary>
    private void RaiseShellChanged()
    {
        OnPropertyChanged(nameof(SelectedTopNav));
        OnPropertyChanged(nameof(SelectedBottomNav));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(ShowSections));
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(SelectedGameTab));
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(CanReturnToGame));
    }
}
