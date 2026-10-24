using System.Collections.ObjectModel;
using System.ComponentModel;
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
}

/// <summary>Item do trilho de navegação. <see cref="IconKey"/> é a chave de um <c>StreamGeometry</c> em <c>Assets/Icons.axaml</c>.</summary>
public sealed partial class NavItemViewModel(NavKey key, string label, string iconKey, bool isEnabled) : ObservableObject
{
    public NavKey Key { get; } = key;

    public string Label { get; } = label;

    public string IconKey { get; } = iconKey;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = isEnabled;

    /// <summary>Falso esconde o item do trilho (o Glossário só aparece com projeto aberto).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>O contador do item (problemas da Conferência); nulo ou zero não mostra nada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge), nameof(BadgeText))]
    public partial int? Badge { get; set; }

    public bool HasBadge => Badge is > 0;

    public string BadgeText => Badge is > 99 ? "99+" : Badge?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>Janela principal: trilho de navegação, página atual e o projeto aberto.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly NavItemViewModel dashboardItem = new(NavKey.Dashboard, "Painel", "Icon.Board", isEnabled: false);
    private readonly NavItemViewModel editorItem = new(NavKey.Editor, "Editor", "Icon.Edit", isEnabled: false);
    private readonly NavItemViewModel conferenceItem = new(NavKey.Conference, "Conferir", "Icon.Shield", isEnabled: false);
    private readonly NavItemViewModel glossaryItem = new(NavKey.Glossary, "Glossário", "Icon.Book", isEnabled: false) { IsVisible = false };
    private readonly NavItemViewModel buildItem = new(NavKey.Build, "Build", "Icon.Box", isEnabled: false);
    private readonly NavItemViewModel settingsItem = new(NavKey.Settings, "Ajustes", "Icon.Settings", isEnabled: true);
    private readonly Action<string> openFolder;
    private readonly FormattingProfileCache profiles = new();
    private bool editorCountIsNewer;
    private NavItemViewModel? selectedNav;
    private EditorViewModel editor = new();
    private bool editorStale;
    private string? lineNotFoundMessage;

    public MainWindowViewModel(StartViewModel start, ResolvedApiKey? apiKey = null, BackgroundTaskService? taskService = null, SettingsViewModel? settings = null, Func<NewProjectWizardViewModel>? newProjectWizard = null, ToastService? toasts = null, Action<string>? openFolder = null)
    {
        this.openFolder = openFolder ?? OpenInFileManager;
        Start = start;
        Toasts = toasts ?? new ToastService();
        Settings = settings ?? new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(Path.Combine(Path.GetTempPath(), "VNForge-sem-ajustes")));
        Settings.KeyChanged += (_, _) => ShowKeyStatus(new ResolvedApiKey(null, Settings.KeyConfigured ? ApiKeySource.Vault : ApiKeySource.None), Settings.KeyStatus);
        TaskService = taskService ?? new BackgroundTaskService();
        Tasks = new TaskBarViewModel(TaskService);
        Build = new BuildViewModel(TaskService);
        var key = apiKey ?? new ResolvedApiKey(null, ApiKeySource.None);
        ShowKeyStatus(key, Services.ApiKeyStatus.Describe(key));
        var startItem = new NavItemViewModel(NavKey.Start, "Início", "Icon.Home", isEnabled: true);
        Nav = [startItem, dashboardItem, editorItem, conferenceItem, buildItem, glossaryItem, settingsItem];
        TopNav = [startItem, dashboardItem, editorItem, conferenceItem, buildItem, glossaryItem];
        BottomNav = [settingsItem];
        selectedNav = startItem;
        CurrentPage = start;
        Palette = new CommandPaletteViewModel(PaletteNamedEntries, PaletteLines);
        Editor.PropertyChanged += OnEditorChanged;
        Editor.CheckRequested += OnEditorCheckRequested;
        Conference.PropertyChanged += OnConferenceChanged;
        Settings.ProjectSaved += (_, _) => ReloadProjectSettings();
        Conference.TextsChanged += (_, _) => editorStale = true;
        AttachEditor(editor);
        Build.NoticeRaised += (_, notice) => ShowBuildNotice(notice);
        Conference.LineRequested += (_, request) =>
        {
            SelectedNav = editorItem;
            LineNavigation = ShowLineAsync(request);
        };
        Build.ErrorsRequested += (_, _) => SelectedNav = conferenceItem;
        start.ProjectOpened += (_, summary) => SetProject(summary);
        start.NewProjectRequested += (_, _) => ShowWizard(newProjectWizard);
        Dashboard.StageRequested += (_, stage) => SelectedNav = stage switch
        {
            PipelineStageKey.Review => conferenceItem,
            PipelineStageKey.Build or PipelineStageKey.Inject => buildItem,
            _ => editorItem,
        };
        Dashboard.StartRequested += (_, _) => SelectedNav = Nav[0];
        Dashboard.NavigationRequested += (_, request) => OpenFromDashboard(request);
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
            if (summary.LastEditedScript is { } script)
            {
                LineNavigation = ShowScriptAsync(script);
            }
        };
        start.DashboardRequested += (_, _) => SelectedNav = dashboardItem;
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
            // O app troca o Editor no inicializador de objeto, depois do construtor: as escutas passam para o novo.
            DetachEditor(editor);
            editor.PropertyChanged -= OnEditorChanged;
            editor.CheckRequested -= OnEditorCheckRequested;
            editor = value;
            AttachEditor(editor);
            editor.PropertyChanged += OnEditorChanged;
            editor.CheckRequested += OnEditorCheckRequested;
        }
    }

    public SettingsViewModel Settings { get; }

    public GlossaryViewModel Glossary { get; } = new();

    public BuildViewModel Build { get; }

    public ConferenceViewModel Conference { get; } = new();

    /// <summary>O último salto para uma linha do Editor pedido pela Conferência; os testes o aguardam.</summary>
    public Task LineNavigation { get; private set; } = Task.CompletedTask;

    /// <summary>A última abertura de projeto criado pelo assistente; os testes a aguardam.</summary>
    public Task ProjectOpening { get; private set; } = Task.CompletedTask;

    /// <summary>A última conferência pedida ao abrir a página; os testes a aguardam.</summary>
    public Task ConferenceLoading { get; private set; } = Task.CompletedTask;

    public ReadOnlyCollection<NavItemViewModel> Nav { get; }

    /// <summary>Os itens do alto do trilho; o Glossário fica escondido até um projeto abrir.</summary>
    public ReadOnlyCollection<NavItemViewModel> TopNav { get; }

    /// <summary>Os itens do pé do trilho (Ajustes).</summary>
    public ReadOnlyCollection<NavItemViewModel> BottomNav { get; }

    public CommandPaletteViewModel Palette { get; }

    [RelayCommand]
    private void OpenPalette() => Palette.Open();

    [ObservableProperty]
    public partial object CurrentPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectTitle), nameof(HasProject), nameof(EncodingText), nameof(BoxLimitsText))]
    public partial ProjectSummary? CurrentProject { get; private set; }

    /// <summary>Texto da barra de status sobre a chave da Anthropic; nunca contém a chave.</summary>
    [ObservableProperty]
    public partial string ApiKeyStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool ApiKeyConfigured { get; private set; }

    public string ProjectTitle => CurrentProject?.Name ?? "Nenhum projeto aberto";

    public bool HasProject => CurrentProject is not null;

    /// <summary>"Salvo às 00:41" depois da primeira gravação do Editor nesta sessão; vazio antes.</summary>
    public string SavedAtText => Editor.LastSavedAt is { } at ? $"Salvo às {at:HH:mm}" : string.Empty;

    /// <summary>"Alterações salvas automaticamente em vnforge.json" enquanto os Ajustes de um projeto estão abertos; vazio nas outras telas.</summary>
    public string StatusNote => SelectedNav?.Key == NavKey.Settings && Settings.HasProject ? SettingsViewModel.AutosaveNote : string.Empty;

    /// <summary>"Acentuação: CP1252" com a estratégia do projeto.</summary>
    public string EncodingText => CurrentProject is { } project ? $"Acentuação: {ProjectLabels.EncodingShort(project.EncodingStrategy)}" : string.Empty;

    /// <summary>"40 colunas · 3 linhas": os limites da caixa de texto do <c>formatting.json</c>; vazio sem projeto ou sem perfil legível.</summary>
    public string BoxLimitsText
    {
        get
        {
            if (CurrentProject is not { } project || profiles.Get(project.Root) is not { } profile)
            {
                return string.Empty;
            }

            var columns = $"{profile.Wrap.MaxWidth} colunas";
            return profile.Wrap.MaxLines is { } lines ? $"{columns} · {lines} {(lines == 1 ? "linha" : "linhas")}" : columns;
        }
    }

    /// <summary>
    /// Os itens do trilho são dois <c>ListBox</c> (alto e pé) e cada um mostra a seleção só dos seus itens; escolher um item do outro grupo
    /// limpa o primeiro, e esse "nada selecionado" não pode mexer na página.
    /// </summary>
    public NavItemViewModel? SelectedTopNav
    {
        get => selectedNav is { } item && TopNav.Contains(item) ? item : null;
        set => SelectFromRail(value);
    }

    public NavItemViewModel? SelectedBottomNav
    {
        get => selectedNav is { } item && BottomNav.Contains(item) ? item : null;
        set => SelectFromRail(value);
    }

    private void SelectFromRail(NavItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedNav = item;
        OnPropertyChanged(nameof(SelectedTopNav));
        OnPropertyChanged(nameof(SelectedBottomNav));
    }

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
                OnPropertyChanged(nameof(SelectedTopNav));
                OnPropertyChanged(nameof(SelectedBottomNav));
                PrepareLeavingAndEntering(value.Key);
                CurrentPage = value.Key switch
                {
                    NavKey.Dashboard => Dashboard,
                    NavKey.Editor => Editor,
                    NavKey.Conference => Conference,
                    NavKey.Settings => Settings,
                    NavKey.Glossary => Glossary,
                    NavKey.Build => Build,
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

                OnPropertyChanged(nameof(BoxLimitsText));
                OnPropertyChanged(nameof(StatusNote));
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

    /// <summary>"Continuar em": abre o script em que o usuário parou, quando ele ainda existe na lista do Editor.</summary>
    private async Task ShowScriptAsync(string scriptKey)
    {
        try
        {
            await Editor.Loading;
        }
        catch (Exception e)
        {
            // O Editor já mostra o erro de carga; sem ele não há script a selecionar.
            Debug.WriteLine(e);
            return;
        }

        if (Editor.Scripts.FirstOrDefault(s => string.Equals(s.Key, scriptKey, StringComparison.Ordinal)) is { } script)
        {
            Editor.SelectedScript = script;
        }
    }

    /// <summary>Um botão do Painel: troca de página e, no Editor, abre o script pedido.</summary>
    private void OpenFromDashboard(DashboardNavigation request)
    {
        if (Nav.FirstOrDefault(n => n.Key == request.Target) is not { IsEnabled: true } item)
        {
            return;
        }

        SelectedNav = item;
        if (request.ScriptKey is { } key && Editor.Scripts.FirstOrDefault(script => script.Key == key) is { } script)
        {
            Editor.SelectedScript = script;
        }
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
        Settings.Load(summary);
        OnPropertyChanged(nameof(StatusNote));
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
        glossaryItem.IsVisible = true;
        UpdateProblemBadge();
        OnPropertyChanged(nameof(SavedAtText));
    }

    private void OnEditorCheckRequested(object? sender, string scriptKey) => SelectedNav = conferenceItem;

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IssueCount))
        {
            editorCountIsNewer = true;
            UpdateProblemBadge();
        }
        else if (e.PropertyName == nameof(EditorViewModel.LastSavedAt))
        {
            OnPropertyChanged(nameof(SavedAtText));
        }
    }

    private void OnConferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConferenceViewModel.IssueCount))
        {
            editorCountIsNewer = false;
            UpdateProblemBadge();
        }
    }

    /// <summary>
    /// O contador vem da última conferência. Antes da primeira, ou depois de o Editor contar de novo (uma edição ou uma releitura), vem das
    /// ocorrências do Editor, que é o número mais recente.
    /// </summary>
    private void UpdateProblemBadge() =>
        conferenceItem.Badge = CurrentProject is null ? null : editorCountIsNewer ? Editor.IssueCount : Conference.IssueCount ?? Editor.IssueCount;

    /// <summary>Ajustes gravou o projeto: relê a acentuação e o modelo do <c>vnforge.json</c> e os limites da caixa do <c>formatting.json</c>.</summary>
    private void ReloadProjectSettings()
    {
        if (CurrentProject is not { } project)
        {
            return;
        }

        try
        {
            var config = ProjectFolder.Open(project.Root).LoadConfig();
            CurrentProject = project with { EncodingStrategy = config.Encoding.Strategy, Model = config.Ai.Model };
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
        }

        OnPropertyChanged(nameof(BoxLimitsText));
    }

    private List<PaletteEntry> PaletteNamedEntries()
    {
        var entries = new List<PaletteEntry>();
        if (CurrentProject is { } project)
        {
            entries.Add(new PaletteEntry(PaletteGroup.Command, "Traduzir pendentes", "Mostra no Editor as linhas ainda sem tradução", () =>
            {
                if (GoTo(editorItem))
                {
                    Editor.ShowPendingLines();
                }
            }));
            entries.Add(new PaletteEntry(PaletteGroup.Command, "Novo build", "Gera a pasta de build do projeto", () =>
            {
                if (GoTo(buildItem) && Build.BuildCommand.CanExecute(null))
                {
                    Build.BuildCommand.Execute(null);
                }
            }));
            entries.Add(new PaletteEntry(PaletteGroup.Command, "Abrir pasta do projeto", project.Root, () => openFolder(project.Root)));
        }

        foreach (var item in Nav.Where(n => n.IsEnabled && n.IsVisible))
        {
            entries.Add(new PaletteEntry(PaletteGroup.Screen, item.Label, "Tela", () => SelectedNav = item));
        }

        foreach (var script in Editor.Scripts)
        {
            entries.Add(new PaletteEntry(PaletteGroup.Script, script.Key, $"{script.LinesLabel} linhas · {script.PercentLabel} concluído", () =>
            {
                if (GoTo(editorItem))
                {
                    Editor.SelectedScript = script;
                }
            }));
        }

        return entries;
    }

    private List<PaletteEntry> PaletteLines(string text, int limit) =>
        [.. Editor.FindLines(text, limit).Select(entry => new PaletteEntry(
            PaletteGroup.Line,
            entry.Unit.Translation is { Length: > 0 } translation ? translation : entry.Unit.Source,
            $"{entry.ScriptKey} · linha {entry.Number}",
            () =>
            {
                if (GoTo(editorItem))
                {
                    LineNavigation = ShowLineAsync(new LineRequest(entry.ScriptKey, entry.Unit.Id));
                }
            }))];

    /// <summary>Troca de página e diz se chegou: a Conferência ocupada ou um painel que não grava recusam a troca.</summary>
    private bool GoTo(NavItemViewModel item)
    {
        SelectedNav = item;
        return ReferenceEquals(SelectedNav, item);
    }

    private static void OpenInFileManager(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // Sem gerenciador de arquivos (sessão remota, CI) o comando não tem outro destino; só registra.
            Debug.WriteLine(e);
        }
    }

    private void ShowKeyStatus(ResolvedApiKey key, string text)
    {
        ApiKeyStatus = text;
        ApiKeyConfigured = key.Source != ApiKeySource.None;
    }
}
