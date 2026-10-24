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
    private NavItemViewModel? selectedNav;

    public MainWindowViewModel(StartViewModel start, ResolvedApiKey? apiKey = null)
    {
        Start = start;
        var key = apiKey ?? new ResolvedApiKey(null, ApiKeySource.None);
        ApiKeyStatus = Services.ApiKeyStatus.Describe(key);
        ApiKeyConfigured = key.Source != ApiKeySource.None;
        Nav = [new NavItemViewModel(NavKey.Start, "Início", "Icon.Home", isEnabled: true), editorItem];
        selectedNav = Nav[0];
        CurrentPage = start;
        start.ProjectOpened += (_, summary) => SetProject(summary);
        start.EditorRequested += (_, summary) =>
        {
            SetProject(summary);
            SelectedNav = editorItem;
        };
    }

    public StartViewModel Start { get; }

    public EditorViewModel Editor { get; } = new();

    public ReadOnlyCollection<NavItemViewModel> Nav { get; }

    [ObservableProperty]
    public partial object CurrentPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectTitle))]
    public partial ProjectSummary? CurrentProject { get; private set; }

    /// <summary>Texto da barra de status sobre a chave da Anthropic; nunca contém a chave.</summary>
    public string ApiKeyStatus { get; }

    public bool ApiKeyConfigured { get; }

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
                CurrentPage = value.Key == NavKey.Editor ? Editor : Start;
            }
        }
    }

    private void SetProject(ProjectSummary summary)
    {
        CurrentProject = summary;
        Editor.Project = summary;
        editorItem.IsEnabled = true;
    }
}
