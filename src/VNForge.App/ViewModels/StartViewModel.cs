using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Tela Início: abrir um projeto, ver os recentes e o progresso do projeto selecionado.</summary>
public sealed partial class StartViewModel(RecentProjectsFile recents, IFolderPicker picker, TimeProvider clock) : ObservableObject
{
    private readonly SemaphoreSlim loadGate = new(1, 1);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedInEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    public partial RecentProjectItem? Selected { get; set; }

    /// <summary>Falha ao abrir um projeto: o fato e a consequência, em português.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Aviso que não é erro, como a lista de recentes que não pôde ser gravada.</summary>
    [ObservableProperty]
    public partial string? NoticeMessage { get; set; }

    public ObservableCollection<RecentProjectItem> Recents { get; } = [];

    public bool HasRecents => Recents.Count > 0;

    public string RecentsSummary => Recents.Count == 1 ? "1 projeto" : $"{Recents.Count} projetos";

    /// <summary>Um projeto foi aberto pelo seletor de pasta.</summary>
    public event EventHandler<ProjectSummary>? ProjectOpened;

    /// <summary>O usuário pediu o assistente de novo projeto.</summary>
    public event EventHandler? NewProjectRequested;

    /// <summary>O usuário pediu para continuar o projeto selecionado no Editor.</summary>
    public event EventHandler<ProjectSummary>? EditorRequested;

    /// <summary>Lê a lista de recentes; mantém a seleção (ou seleciona <paramref name="selectPath"/>) quando o item ainda existe.</summary>
    public async Task LoadAsync(string? selectPath = null)
    {
        // Duas cargas ao mesmo tempo (abertura da janela e um projeto aberto logo em seguida) embaralhariam a lista.
        await loadGate.WaitAsync();
        try
        {
            var keep = selectPath ?? Selected?.Path;
            var now = clock.GetUtcNow();
            var items = await Task.WhenAll(recents.Load().Select(recent => Task.Run(() => Describe(recent, now))));

            Recents.Clear();
            foreach (var item in items)
            {
                Recents.Add(item);
            }

            Selected = Recents.FirstOrDefault(i => SamePath(i.Path, keep)) ?? Recents.FirstOrDefault();
            OnPropertyChanged(nameof(HasRecents));
            OnPropertyChanged(nameof(RecentsSummary));
        }
        finally
        {
            loadGate.Release();
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        ClearMessages();
        var path = await picker.PickFolderAsync("Abrir projeto do VNForge");
        if (path is not null)
        {
            await OpenAsync(path);
        }
    }

    /// <summary>Abre a pasta, registra nos recentes e seleciona. Em caso de falha, mostra <see cref="ErrorMessage"/> e não registra nada.</summary>
    public async Task OpenAsync(string path)
    {
        ProjectSummary summary;
        try
        {
            summary = await Task.Run(() => ProjectSummaryReader.Read(path));
        }
        catch (Exception e) when (IsExpected(e))
        {
            ErrorMessage = $"{e.Message} O projeto não foi aberto.";
            return;
        }

        try
        {
            recents.Add(summary.Root, clock.GetUtcNow());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A lista de recentes é conveniência: o projeto abre mesmo sem ela.
            NoticeMessage = "O projeto abriu, mas não foi possível gravar a lista de recentes.";
        }

        await LoadAsync(summary.Root);
        ProjectOpened?.Invoke(this, summary);
    }

    [RelayCommand]
    private void NewProject()
    {
        ClearMessages();
        NewProjectRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private void OpenSelectedInEditor()
    {
        if (Selected?.Summary is { } summary)
        {
            EditorRequested?.Invoke(this, summary);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveSelectedAsync()
    {
        if (Selected is { } item)
        {
            recents.Remove(item.Path);
            Selected = null;
            await LoadAsync();
        }
    }

    private bool CanOpenSelected() => Selected?.IsAvailable == true;

    private bool HasSelection() => Selected is not null;

    private void ClearMessages()
    {
        ErrorMessage = null;
        NoticeMessage = null;
    }

    private static RecentProjectItem Describe(RecentProject recent, DateTimeOffset now)
    {
        if (!Directory.Exists(recent.Path))
        {
            return RecentProjectItem.FromProblem(recent.Path, $"O projeto não foi encontrado em '{recent.Path}'. Remova-o da lista ou restaure a pasta.", recent.OpenedAt, now);
        }

        try
        {
            return RecentProjectItem.FromSummary(ProjectSummaryReader.Read(recent.Path), recent.OpenedAt, now);
        }
        catch (Exception e) when (IsExpected(e))
        {
            return RecentProjectItem.FromProblem(recent.Path, e.Message, recent.OpenedAt, now);
        }
    }

    private static bool IsExpected(Exception e) => e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException;

    private static bool SamePath(string a, string? b) =>
        b is not null && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
