using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public enum NavKey
{
    Start,
    Editor,
    Settings,
    Glossary,
    Build,
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
    private readonly NavItemViewModel editorItem = new(NavKey.Editor, "Editor", "Icon.Edit", isEnabled: false);
    private readonly NavItemViewModel glossaryItem = new(NavKey.Glossary, "Glossário", "Icon.Book", isEnabled: false);
    private readonly NavItemViewModel buildItem = new(NavKey.Build, "Build", "Icon.Box", isEnabled: false);
    private NavItemViewModel? selectedNav;

    public MainWindowViewModel(StartViewModel start, ResolvedApiKey? apiKey = null, BackgroundTaskService? taskService = null, SettingsViewModel? settings = null, Func<NewProjectWizardViewModel>? newProjectWizard = null)
    {
        Start = start;
        Settings = settings ?? new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(Path.Combine(Path.GetTempPath(), "VNForge-sem-ajustes")));
        Settings.KeyChanged += (_, _) => ShowKeyStatus(new ResolvedApiKey(null, Settings.KeyConfigured ? ApiKeySource.Vault : ApiKeySource.None), Settings.KeyStatus);
        TaskService = taskService ?? new BackgroundTaskService();
        Tasks = new TaskBarViewModel(TaskService);
        Build = new BuildViewModel(TaskService);
        var key = apiKey ?? new ResolvedApiKey(null, ApiKeySource.None);
        ShowKeyStatus(key, Services.ApiKeyStatus.Describe(key));
        Nav = [new NavItemViewModel(NavKey.Start, "Início", "Icon.Home", isEnabled: true), editorItem, glossaryItem, buildItem, new NavItemViewModel(NavKey.Settings, "Ajustes", "Icon.Settings", isEnabled: true)];
        selectedNav = Nav[0];
        CurrentPage = start;
        start.ProjectOpened += (_, summary) => SetProject(summary);
        start.NewProjectRequested += (_, _) => ShowWizard(newProjectWizard);
        start.EditorRequested += (_, summary) =>
        {
            SetProject(summary);
            SelectedNav = editorItem;
        };
    }

    public StartViewModel Start { get; }

    /// <summary>Onde as telas iniciam extrair, traduzir, build e injetar sem travar a edição.</summary>
    public BackgroundTaskService TaskService { get; }

    public TaskBarViewModel Tasks { get; }

    public EditorViewModel Editor { get; } = new();

    public SettingsViewModel Settings { get; }

    public GlossaryViewModel Glossary { get; } = new();

    public BuildViewModel Build { get; }

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

            if (SetProperty(ref selectedNav, value))
            {
                CurrentPage = value.Key switch
                {
                    NavKey.Editor => Editor,
                    NavKey.Settings => Settings,
                    NavKey.Glossary => Glossary,
                    NavKey.Build => Build,
                    _ => Start,
                };
                if (value.Key == NavKey.Build)
                {
                    _ = Build.ReloadAsync();
                }
            }
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
        wizard.Completed += async (_, root) =>
        {
            CurrentPage = Start;
            await Start.OpenAsync(root);
        };
        CurrentPage = wizard;
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
        CurrentProject = summary;
        Editor.Project = summary;
        Settings.Load(summary);
        // Reabrir o mesmo projeto não pode descartar o que o usuário editou e ainda não salvou.
        if (!Glossary.IsDirty || Glossary.Project?.Root != summary.Root)
        {
            Glossary.Project = summary;
        }

        Build.Project = summary;
        editorItem.IsEnabled = true;
        glossaryItem.IsEnabled = true;
        buildItem.IsEnabled = true;
    }

    private void ShowKeyStatus(ResolvedApiKey key, string text)
    {
        ApiKeyStatus = text;
        ApiKeyConfigured = key.Source != ApiKeySource.None;
    }
}
