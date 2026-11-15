using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// A aba Backups dos Ajustes: a pasta onde ficam os backups de todos os projetos, cada um numa subpasta com o nome dele. Vale para a Biblioteca inteira e fica no
/// <c>library.json</c>, o mesmo que <c>vnf library backup-folder</c> grava. Trocar a pasta não move nem apaga backup: os de antes continuam na lista e na restauração.
/// </summary>
public sealed partial class BackupSettingsViewModel : ObservableObject
{
    private const string SampleFolder = @"D:\Backups\VNForge";

    private readonly LibraryFile file;
    private readonly IFolderPicker picker;
    private string? projectRoot;
    private string? projectName;
    private string saved = string.Empty;

    public BackupSettingsViewModel(LibraryFile file, IFolderPicker picker)
    {
        this.file = file;
        this.picker = picker;
        Load();
    }

    /// <summary>A pasta dos backups; vazio é o padrão, <c>backup\</c> dentro de cada projeto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExampleText), nameof(CanSave))]
    public partial string PathText { get; set; } = string.Empty;

    /// <summary>Por que a pasta não foi aceita, em vermelho sob o campo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Quantos backups do projeto aberto ficaram nas pastas anteriores; nulo sem projeto ou sem backup lá.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExisting))]
    public partial string? ExistingText { get; private set; }

    public bool HasExisting => !string.IsNullOrEmpty(ExistingText);

    /// <summary>O caminho de um backup, com o nome do projeto aberto (ou um nome de exemplo).</summary>
    public string ExampleText =>
        Path.Combine(string.IsNullOrWhiteSpace(PathText) ? SampleFolder : PathText.Trim(), projectName ?? "Minha VN", "AAAA-MM-DD_HHmm");

    public bool CanSave => !string.Equals(PathText.Trim(), saved, StringComparison.Ordinal);

    /// <summary>Relê o <c>library.json</c>: a pasta pode ter mudado pela CLI.</summary>
    public void Load()
    {
        try
        {
            saved = file.Load().BackupFolder ?? string.Empty;
            PathText = saved;
            ErrorMessage = null;
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
        }

        OnPropertyChanged(nameof(CanSave));
        ReadExisting();
    }

    /// <summary>O projeto aberto: o exemplo usa o nome dele e o aviso conta os backups que ficaram para trás.</summary>
    public void SetProject(string? root)
    {
        projectRoot = root;
        projectName = null;
        if (root is not null)
        {
            try
            {
                projectName = BackupLocations.ProjectSubfolder(ProjectFolder.Open(root));
            }
            catch (VNForgeException)
            {
                projectRoot = null;
            }
        }

        OnPropertyChanged(nameof(ExampleText));
        ReadExisting();
    }

    /// <summary>Confere a pasta e grava; em branco, volta ao padrão.</summary>
    [RelayCommand]
    private void Save()
    {
        try
        {
            file.Update(library => library.SetBackupFolder(PathText.Trim()));
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
            return;
        }

        Load();
    }

    [RelayCommand]
    private void UseDefault()
    {
        PathText = string.Empty;
        Save();
    }

    [RelayCommand]
    private async Task ChooseAsync()
    {
        if (await picker.PickFolderAsync("Pasta dos backups") is { } folder)
        {
            PathText = folder;
            ErrorMessage = null;
        }
    }

    partial void OnPathTextChanged(string value) => ErrorMessage = null;

    private void ReadExisting()
    {
        ExistingText = null;
        if (projectRoot is null)
        {
            return;
        }

        try
        {
            var locations = UserBackupLocations.Read(file);
            if (!locations.IsCustom)
            {
                return;
            }

            var project = ProjectFolder.Open(projectRoot).WithBackups(locations);
            var current = locations.CurrentRoot(project);
            var left = GameInjector.ListBackups(project)
                .Where(b => !string.Equals(b.Root, current, StringComparison.OrdinalIgnoreCase))
                .GroupBy(b => b.Root, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (left.Count == 0)
            {
                return;
            }

            var total = left.Sum(g => g.Count());
            var where = string.Join(" e ", left.Select(g => g.Key));
            var old = left.Count == 1 ? "a pasta anterior" : "as pastas anteriores";
            ExistingText = $"{ProjectLabels.Number(total)} {(total == 1 ? "backup ficou" : "backups ficaram")} em {where}, {old}. Continuam na lista de Backups e na restauração. O VNForge não move nem apaga nada ao trocar de pasta.";
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ExistingText = null;
        }
    }
}
