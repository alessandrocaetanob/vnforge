using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>O projeto que já está na pasta escolhida no passo "Idiomas e nome": o que o cartão "Projeto encontrado" mostra.</summary>
public sealed record ExistingProjectInfo(string Folder, string Name, string GameFolder, string Languages, string Progress, double ProgressFraction);

/// <summary>Pasta do projeto que já tem <c>vnforge.json</c>: em vez de recusar no fim, o passo mostra o projeto e oferece abri-lo.</summary>
public sealed partial class NewProjectWizardViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExistingProject), nameof(ShowNewProjectFields), nameof(ShowOpenExisting), nameof(ShowNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(OpenExistingProjectCommand))]
    public partial ExistingProjectInfo? ExistingProject { get; private set; }

    public bool HasExistingProject => ExistingProject is not null;

    /// <summary>Nome e idiomas só valem para um projeto novo; um projeto encontrado já os tem.</summary>
    public bool ShowNewProjectFields => !HasExistingProject;

    /// <summary>No passo do nome com um projeto encontrado, "Abrir este projeto" ocupa o lugar de "Avançar".</summary>
    public bool ShowOpenExisting => IsNamesStep && HasExistingProject;

    public bool ShowNext => !ShowOpenExisting;

    /// <summary>"Abrir este projeto": o shell liga o projeto ao jogo da Biblioteca e o abre. O assistente não grava nada.</summary>
    public event EventHandler<string>? ExistingProjectChosen;

    [RelayCommand(CanExecute = nameof(HasExistingProject))]
    private void OpenExistingProject()
    {
        if (ExistingProject is { } existing)
        {
            ExistingProjectChosen?.Invoke(this, existing.Folder);
        }
    }

    private void FindExistingProject(string folder)
    {
        try
        {
            if (!File.Exists(Path.Combine(folder, ProjectFolder.ConfigFileName)))
            {
                ExistingProject = null;
                return;
            }

            var project = ProjectFolder.Open(folder);
            var config = project.LoadConfig();
            var total = ProjectStatusReporter.Create(project).Total;
            var done = $"{total.Done.ToString("N0", PtBr)} de {total.Total.ToString("N0", PtBr)} linhas";
            var percent = Math.Floor(total.Percent);
            ExistingProject = new ExistingProjectInfo(
                project.Root,
                config.Name,
                Path.GetFullPath(config.Game.Path, project.Root),
                $"{LanguageName(SourceLanguages, config.Languages.Source)} → {LanguageName(TargetLanguages, config.Languages.Target)}",
                $"{done} · {percent:0}%",
                percent / 100);
        }
        catch (Exception e) when (IsExpected(e))
        {
            // Um vnforge.json que não abre não vira "projeto encontrado": o erro de criar o projeto diz o que está errado.
            ExistingProject = null;
        }
    }

    private static string LanguageName(ChoiceGroup group, string id) => group.Options.FirstOrDefault(o => o.Id == id)?.Title ?? id;
}
