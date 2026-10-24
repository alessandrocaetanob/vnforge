using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public enum NavKey
{
    Start,
    Dashboard,
    Editor,
    Conference,
    Settings,
    Glossary,
    Build,
    Search,
}

/// <summary>Item do trilho de navegação. <see cref="IconKey"/> é a chave de um <c>StreamGeometry</c> em <c>Assets/Icons.axaml</c>.</summary>
public sealed partial class NavItemViewModel(NavKey key, string label, string iconKey, bool isEnabled) : ObservableObject
{
    public NavKey Key { get; } = key;

    public string Label { get; } = label;

    public string IconKey { get; } = iconKey;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = isEnabled;
}

/// <summary>Janela principal: trilho de navegação, página atual e o projeto aberto.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly NavItemViewModel dashboardItem = new(NavKey.Dashboard, "Painel", "Icon.Board", isEnabled: false);
    private readonly NavItemViewModel editorItem = new(NavKey.Editor, "Editor", "Icon.Edit", isEnabled: false);
    private readonly NavItemViewModel conferenceItem = new(NavKey.Conference, "Conferência", "Icon.Shield", isEnabled: false);
    private readonly NavItemViewModel glossaryItem = new(NavKey.Glossary, "Glossário", "Icon.Book", isEnabled: false);
    private readonly NavItemViewModel buildItem = new(NavKey.Build, "Build", "Icon.Box", isEnabled: false);
    private readonly NavItemViewModel searchItem = new(NavKey.Search, "Busca", "Icon.Search", isEnabled: false);
    private NavItemViewModel? selectedNav;
    private EditorViewModel editor = new();
    private bool editorStale;
    private string? lineNotFoundMessage;

    public MainWindowViewModel(StartViewModel start, ResolvedApiKey? apiKey = null, BackgroundTaskService? taskService = null, SettingsViewModel? settings = null, Func<NewProjectWizardViewModel>? newProjectWizard = null, ToastService? toasts = null)
    {
        Start = start;
        Toasts = toasts ?? new ToastService();
        Settings = settings ?? new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(Path.Combine(Path.GetTempPath(), "VNForge-sem-ajustes")));
        Settings.KeyChanged += (_, _) => ShowKeyStatus(new ResolvedApiKey(null, Settings.KeyConfigured ? ApiKeySource.Vault : ApiKeySource.None), Settings.KeyStatus);
        TaskService = taskService ?? new BackgroundTaskService();
        Tasks = new TaskBarViewModel(TaskService);
        Build = new BuildViewModel(TaskService);
        var key = apiKey ?? new ResolvedApiKey(null, ApiKeySource.None);
        ShowKeyStatus(key, Services.ApiKeyStatus.Describe(key));
        Nav = [new NavItemViewModel(NavKey.Start, "Início", "Icon.Home", isEnabled: true), dashboardItem, editorItem, conferenceItem, searchItem, buildItem, glossaryItem, new NavItemViewModel(NavKey.Settings, "Ajustes", "Icon.Settings", isEnabled: true)];
        selectedNav = Nav[0];
        CurrentPage = start;
        Search.FlushPendingEdits = Editor.FlushPendingEdits;
        Search.Applied += (_, _) => Editor.Reload();
        Conference.TextsChanged += (_, _) => editorStale = true;
        AttachEditor(editor);
        Build.NoticeRaised += (_, notice) => ShowBuildNotice(notice);
        Conference.LineRequested += (_, request) =>
        {
            SelectedNav = editorItem;
            LineNavigation = ShowLineAsync(request);
        };
        start.ProjectOpened += (_, summary) => SetProject(summary);
        start.NewProjectRequested += (_, _) => ShowWizard(newProjectWizard);
        Dashboard.StageRequested += (_, stage) => SelectedNav = stage switch
        {
            PipelineStageKey.Review => conferenceItem,
            PipelineStageKey.Build or PipelineStageKey.Inject => buildItem,
            _ => editorItem,
        };
        Dashboard.StartRequested += (_, _) => SelectedNav = Nav[0];
        start.EditorRequested += (_, summary) =>
        {
            try
            {
                SetProject(summary);
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                start.ErrorMessage = $"{e.Message} O projeto não pôde ser carregado.";
                return;
            }

            SelectedNav = editorItem;
        };
    }

    public StartViewModel Start { get; }

    /// <summary>Avisos curtos de gravação, build pronto e injeção.</summary>
    public ToastService Toasts { get; }

    /// <summary>O mapa de atalhos (tecla <c>?</c>) está aberto.</summary>
    [ObservableProperty]
    public partial bool IsShortcutMapOpen { get; private set; }

    /// <summary>Onde as telas iniciam extrair, traduzir, build e injetar sem travar a edição.</summary>
    public BackgroundTaskService TaskService { get; }

    public TaskBarViewModel Tasks { get; }

    public DashboardViewModel Dashboard { get; } = new();

    public EditorViewModel Editor
    {
        get => editor;
        init
        {
            DetachEditor(editor);
            editor = value;
            AttachEditor(editor);
        }
    }

    public SettingsViewModel Settings { get; }

    public GlossaryViewModel Glossary { get; } = new();

    public BuildViewModel Build { get; }

    public SearchReplaceViewModel Search { get; } = new();

    public ConferenceViewModel Conference { get; } = new();

    /// <summary>O último salto para uma linha do Editor pedido pela Conferência; os testes o aguardam.</summary>
    public Task LineNavigation { get; private set; } = Task.CompletedTask;

    /// <summary>A última abertura de projeto criado pelo assistente; os testes a aguardam.</summary>
    public Task ProjectOpening { get; private set; } = Task.CompletedTask;

    /// <summary>A última conferência pedida ao abrir a página; os testes a aguardam.</summary>
    public Task ConferenceLoading { get; private set; } = Task.CompletedTask;

    public ReadOnlyCollection<NavItemViewModel> Nav { get; }

    [ObservableProperty]
    public partial object CurrentPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectTitle))]
    public partial ProjectSummary? CurrentProject { get; private set; }

    /// <summary>Texto da barra de status sobre a chave da Anthropic; nunca contém a chave.</summary>
    [ObservableProperty]
    public partial string ApiKeyStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool ApiKeyConfigured { get; private set; }

    public string ProjectTitle => CurrentProject?.Name ?? "Nenhum projeto aberto";

    /// <summary>Item selecionado no trilho; um item desabilitado não é aceito e a seleção volta ao anterior.</summary>
    public NavItemViewModel? SelectedNav
    {
        get => selectedNav;
        set
        {
            if (value is null || !value.IsEnabled)
            {
                OnPropertyChanged();
                return;
            }

            if (!ReferenceEquals(value, selectedNav) && !CanSwitchTo(value.Key))
            {
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref selectedNav, value))
            {
                PrepareLeavingAndEntering(value.Key);
                CurrentPage = value.Key switch
                {
                    NavKey.Dashboard => Dashboard,
                    NavKey.Editor => Editor,
                    NavKey.Conference => Conference,
                    NavKey.Settings => Settings,
                    NavKey.Glossary => Glossary,
                    NavKey.Build => Build,
                    NavKey.Search => Search,
                    _ => Start,
                };
                if (value.Key == NavKey.Dashboard)
                {
                    _ = Dashboard.ReloadAsync();
                }
                else if (value.Key == NavKey.Build)
                {
                    _ = Build.ReloadAsync();
                }
            }
        }
    }

    /// <summary>
    /// A Conferência reescreve <c>text/*.json</c> inteiros, então a troca de página espera: não sai dela enquanto ela lê ou corrige, e só entra
    /// nela depois de gravar a edição pendente do painel do Editor (se a gravação falha, o painel mostra o erro e a página não troca).
    /// </summary>
    private bool CanSwitchTo(NavKey target)
    {
        if (ReferenceEquals(CurrentPage, Conference) && Conference.IsBusy)
        {
            return false;
        }

        return target != NavKey.Conference || Editor.Detail?.Flush() != false;
    }

    private void AttachEditor(EditorViewModel value) => value.LineSaved += OnLineSaved;

    private void DetachEditor(EditorViewModel value) => value.LineSaved -= OnLineSaved;

    private void OnLineSaved(object? sender, LineSavedEventArgs saved) => ShowLineSaved(saved);

    [RelayCommand]
    private void ToggleShortcutMap() => IsShortcutMapOpen = !IsShortcutMapOpen;

    [RelayCommand]
    private void CloseShortcutMap() => IsShortcutMapOpen = false;

    private void ShowLineSaved(LineSavedEventArgs saved)
    {
        var text = saved.IsApproval ? $"Linha {saved.Number} aprovada." : $"Linha {saved.Number} gravada.";
        Toasts.Show(text, ToastKind.Success, key: "line-save", undo: () => UndoLineSave(saved));
    }

    private void UndoLineSave(LineSavedEventArgs saved)
    {
        if (!Editor.Undo(saved))
        {
            Toasts.Show(Editor.ErrorMessage ?? $"Não foi possível desfazer a gravação da linha {saved.Number}.", ToastKind.Error, key: "line-undo");
        }
    }

    public void ShowBuildNotice(BuildNotice notice)
    {
        if (notice.Kind == BuildNoticeKind.Injected)
        {
            // Desfazer a injeção é restaurar os originais, e a restauração pede confirmação na página Build, como sempre.
            Toasts.Show(notice.Message, ToastKind.Success, key: "build", undo: () =>
            {
                SelectedNav = buildItem;
                if (Build.RestoreCommand.CanExecute(null))
                {
                    Build.RestoreCommand.Execute(null);
                }
                else
                {
                    Toasts.Show("Não dá para restaurar os originais agora. Espere a operação em andamento terminar e use Restaurar na página Build.", ToastKind.Warning, key: "build-restore");
                }
            });
            return;
        }

        Toasts.Show(notice.Message, ToastKind.Success, key: "build");
    }

    private async Task ShowLineAsync(LineRequest request)
    {
        var found = await Editor.GoToLineAsync(request.ScriptKey, request.UnitId);
        if (!found)
        {
            lineNotFoundMessage = $"A linha {request.UnitId} não existe mais neste projeto. Confira de novo na Conferência.";
            Editor.ErrorMessage = lineNotFoundMessage;
        }
        else if (lineNotFoundMessage is not null && Editor.ErrorMessage == lineNotFoundMessage)
        {
            Editor.ErrorMessage = null;
        }
    }

    /// <summary>A Conferência é lida ao abrir a página; ao voltar ao Editor, ele relê se a Conferência mudou algum texto.</summary>
    private void PrepareLeavingAndEntering(NavKey target)
    {
        if (target == NavKey.Conference)
        {
            ConferenceLoading = Conference.RefreshAsync();
        }
        else if (target == NavKey.Editor && editorStale)
        {
            editorStale = false;
            Editor.Reload();
        }
    }

    /// <summary>Troca a página pelo assistente de novo projeto; ao terminar ou cancelar volta ao Início, e um projeto criado é aberto.</summary>
    private void ShowWizard(Func<NewProjectWizardViewModel>? create)
    {
        if (create is null)
        {
            return;
        }

        var wizard = create();
        wizard.Cancelled += (_, _) => CurrentPage = Start;
        wizard.Completed += (_, root) =>
        {
            CurrentPage = Start;
            ProjectOpening = OpenCreatedProjectAsync(root);
        };
        CurrentPage = wizard;
    }

    private async Task OpenCreatedProjectAsync(string root)
    {
        try
        {
            await Start.OpenAsync(root);
        }
        catch (Exception e)
        {
            // Os erros esperados já viram mensagem em Start.OpenAsync; o resto é defeito: sem esta rede a Task falha ficaria sem observador.
            Debug.WriteLine(e);
            Start.ErrorMessage = "O projeto foi criado, mas ocorreu um erro inesperado ao abri-lo. Abra a pasta pelo Início.";
        }
    }

    /// <summary>Pergunta se a janela pode fechar. Com a injeção ou a restauração escrevendo no jogo, não: mostra a página Build com o aviso.</summary>
    public bool TryClose()
    {
        if (Build.TryCloseSafely())
        {
            return true;
        }

        SelectedNav = buildItem;
        return false;
    }

    private void SetProject(ProjectSummary summary)
    {
        if (CurrentProject?.Root != summary.Root)
        {
            Toasts.Clear();
        }

        CurrentProject = summary;
        Dashboard.Project = summary;
        Editor.Project = summary;
        Search.Project = summary;
        Settings.Load(summary);
        // Reabrir o mesmo projeto não pode descartar o que o usuário editou e ainda não salvou.
        if (!Glossary.IsDirty || Glossary.Project?.Root != summary.Root)
        {
            Glossary.Project = summary;
        }

        Build.Project = summary;
        Conference.Project = summary;
        editorStale = false;
        dashboardItem.IsEnabled = true;
        editorItem.IsEnabled = true;
        conferenceItem.IsEnabled = true;
        glossaryItem.IsEnabled = true;
        buildItem.IsEnabled = true;
        searchItem.IsEnabled = true;
    }

    private void ShowKeyStatus(ResolvedApiKey key, string text)
    {
        ApiKeyStatus = text;
        ApiKeyConfigured = key.Source != ApiKeySource.None;
    }
}
