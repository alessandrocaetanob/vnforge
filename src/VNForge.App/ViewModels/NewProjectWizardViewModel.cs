using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Os passos do assistente de novo projeto, na ordem do doc 04.</summary>
public enum WizardStep
{
    Game,
    Names,
    Accents,
    Ai,
    Done,
}

/// <summary>Uma opção de escolha única do assistente (idioma, acentuação, modelo): o <see cref="Id"/> é o que vai para o <c>vnforge.json</c>.</summary>
public sealed partial class ChoiceOption(string id, string title, string value = "", string description = "") : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    /// <summary>Texto à direita do cartão, como o preço do modelo.</summary>
    public string Value { get; } = value;

    public string Description { get; } = description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Lista de <see cref="ChoiceOption"/> em que só uma fica marcada; ligada aos <c>RadioButton</c> dos cartões.</summary>
public sealed class ChoiceGroup
{
    public ChoiceGroup(IEnumerable<ChoiceOption> options, string selectedId)
    {
        Options = new ReadOnlyCollection<ChoiceOption>([.. options]);
        foreach (var option in Options)
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChoiceOption.IsSelected) && option.IsSelected)
                {
                    Select(option);
                }
            };
        }

        Select(Options.First(o => o.Id == selectedId));
    }

    public ReadOnlyCollection<ChoiceOption> Options { get; }

    public ChoiceOption Selected { get; private set; } = null!;

    public event EventHandler? SelectionChanged;

    public void Select(string id) => Select(Options.First(o => o.Id == id));

    private void Select(ChoiceOption option)
    {
        Selected = option;
        foreach (var other in Options)
        {
            other.IsSelected = ReferenceEquals(other, option);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Assistente de novo projeto: pasta do jogo com detecção, idiomas e nome, acentuação e IA; no fim, oferece extrair.
/// Cria o projeto com o mesmo <see cref="ProjectInitializer"/> do <c>vnf init</c>.
/// </summary>
public sealed partial class NewProjectWizardViewModel : ObservableObject
{
    private readonly IFolderPicker picker;
    private readonly IReadOnlyList<IEngine> engines;
    private GameInspection? inspection;

    public NewProjectWizardViewModel(IFolderPicker picker, IReadOnlyList<IEngine> engines)
    {
        this.picker = picker;
        this.engines = engines;
        SourceLanguages = new ChoiceGroup(
            [new("ja", "Japonês"), new("en", "Inglês"), new("zh", "Chinês"), new("ko", "Coreano")], ProjectChoices.DefaultSourceLanguage);
        TargetLanguages = new ChoiceGroup(
            [new("pt-BR", "Português do Brasil"), new("en", "Inglês"), new("es", "Espanhol")], ProjectChoices.DefaultTargetLanguage);
        EncodingStrategies = new ChoiceGroup(
            ProjectChoices.EncodingStrategies.Where(s => s != "sjis").Select(EncodingOption), ProjectChoices.DefaultEncoding);
        Models = new ChoiceGroup(ProjectChoices.Models.Select(ModelOption), ProjectChoices.DefaultAi.Model);
    }

    public ChoiceGroup SourceLanguages { get; }

    public ChoiceGroup TargetLanguages { get; }

    public ChoiceGroup EncodingStrategies { get; }

    public ChoiceGroup Models { get; }

    /// <summary>Linhas da detecção do passo 1: engine, arquivo de scripts, chave.</summary>
    public ObservableCollection<string> DetectionLines { get; } = [];

    /// <summary>Executáveis candidatos; com mais de um, o usuário escolhe.</summary>
    public ObservableCollection<string> Executables { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameStep), nameof(IsNamesStep), nameof(IsAccentsStep), nameof(IsAiStep), nameof(IsDoneStep), nameof(StepNumber), nameof(StepTitle), nameof(StepLabel), nameof(CanGoBack), nameof(IsWizardStep), nameof(NextLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial WizardStep Step { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame), nameof(GameFolderLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? GameFolder { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? SelectedExe { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string ProjectName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectDirectoryLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? ProjectDirectory { get; private set; }

    /// <summary>No passo de IA, "pular" grava o padrão do <c>vnf init</c> em vez do modelo marcado.</summary>
    [ObservableProperty]
    public partial bool SkipAi { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(ExtractCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>O projeto criado; vale a partir do passo final.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreatedSummary))]
    public partial ProjectResultInfo? Created { get; private set; }

    /// <summary>Resultado da extração oferecida no fim, em uma frase; nulo antes de rodar.</summary>
    [ObservableProperty]
    public partial string? ExtractionMessage { get; private set; }

    [ObservableProperty]
    public partial bool IsExtracted { get; private set; }

    public string GameFolderLabel => GameFolder ?? "Nenhuma pasta escolhida";

    public string ProjectDirectoryLabel => ProjectDirectory ?? "Nenhuma pasta escolhida";

    public string StepLabel => $"Passo {StepNumber} de 4 · {StepTitle}";

    public bool HasExecutables => Executables.Count > 1;

    public bool HasGame => GameFolder is not null;

    public bool IsGameStep => Step == WizardStep.Game;

    public bool IsNamesStep => Step == WizardStep.Names;

    public bool IsAccentsStep => Step == WizardStep.Accents;

    public bool IsAiStep => Step == WizardStep.Ai;

    public bool IsDoneStep => Step == WizardStep.Done;

    public bool IsWizardStep => Step != WizardStep.Done;

    public bool CanGoBack => Step is not (WizardStep.Game or WizardStep.Done);

    public int StepNumber => (int)Step + 1;

    public string StepTitle => Step switch
    {
        WizardStep.Game => "Pasta do jogo",
        WizardStep.Names => "Idiomas e nome",
        WizardStep.Accents => "Acentuação",
        WizardStep.Ai => "Provedor de IA",
        _ => "Projeto criado",
    };

    public string NextLabel => Step == WizardStep.Ai ? "Criar projeto" : "Avançar";

    public string CreatedSummary => Created is { } c ? $"{c.Name} · {c.Folder}" : string.Empty;

    /// <summary>O usuário terminou (ou fechou) o assistente com o projeto criado em <see cref="ProjectDirectory"/>.</summary>
    public event EventHandler<string>? Completed;

    /// <summary>O usuário cancelou antes de criar o projeto.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Roda a detecção na pasta do jogo e mostra o que achou. Falha mostra <see cref="ErrorMessage"/> e não avança.</summary>
    public async Task SetGameFolderAsync(string path)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            inspection = await Task.Run(() => ProjectInitializer.Inspect(path, engines));
        }
        catch (Exception e) when (IsExpected(e))
        {
            inspection = null;
            GameFolder = null;
            DetectionLines.Clear();
            Executables.Clear();
            OnPropertyChanged(nameof(HasExecutables));
            SelectedExe = null;
            ErrorMessage = $"{e.Message} Escolha outra pasta.";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        GameFolder = Path.GetFullPath(path);
        DescribeDetection(inspection);
        Executables.Clear();
        foreach (var exe in inspection.Executables)
        {
            Executables.Add(exe);
        }

        OnPropertyChanged(nameof(HasExecutables));

        SelectedExe = inspection.Executables.Count == 1 ? inspection.Executables[0] : null;
        if (inspection.Executables.Count == 0)
        {
            ErrorMessage = $"Nenhum executável (.exe) em '{GameFolder}'. Escolha a pasta que tem o executável do jogo.";
        }
    }

    /// <summary>Define a pasta do projeto; sem nome digitado, o nome do projeto vira o da pasta (como o <c>vnf init</c>).</summary>
    public void SetProjectDirectory(string path)
    {
        ErrorMessage = null;
        var previous = FolderNameOf(ProjectDirectory);
        ProjectDirectory = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(ProjectName) || ProjectName == previous)
        {
            ProjectName = FolderNameOf(ProjectDirectory);
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseGameAsync()
    {
        var path = await picker.PickFolderAsync("Pasta do jogo a traduzir");
        if (path is not null)
        {
            await SetGameFolderAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseProjectAsync()
    {
        var path = await picker.PickFolderAsync("Pasta do novo projeto");
        if (path is not null)
        {
            SetProjectDirectory(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        ErrorMessage = null;
        if (Step == WizardStep.Ai)
        {
            await CreateAsync();
        }
        else
        {
            Step++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBackNow))]
    private void Back()
    {
        ErrorMessage = null;
        Step--;
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>Fecha o assistente com o projeto pronto e abre-o no app.</summary>
    [RelayCommand]
    private void Finish()
    {
        if (Created is { } created)
        {
            Completed?.Invoke(this, created.Folder);
        }
    }

    /// <summary>Extrai o jogo do projeto recém-criado (roda fora da thread da tela, sem progresso nem cancelar; o segundo plano de verdade vem com a barra de tarefas, #101).</summary>
    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync()
    {
        if (Created is not { } created)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => ProjectExtractor.Extract(ProjectFolder.Open(created.Folder), engines));
            IsExtracted = true;
            ExtractionMessage = $"Extração concluída: {ProjectLabels.Number(result.TotalLines)} linhas em {ProjectLabels.Number(result.Scripts.Count)} scripts.";
        }
        catch (Exception e) when (IsExpected(e))
        {
            ErrorMessage = $"{e.Message} A extração não terminou; rode de novo no painel do projeto.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanBrowse() => !IsBusy;

    private bool CanExtract() => !IsBusy && !IsExtracted && Created is not null;

    private bool CanGoBackNow() => !IsBusy && CanGoBack;

    private bool CanGoNext() => !IsBusy && Step switch
    {
        WizardStep.Game => HasGame && SelectedExe is not null,
        WizardStep.Names => !string.IsNullOrWhiteSpace(ProjectName) && ProjectDirectory is not null,
        WizardStep.Accents or WizardStep.Ai => true,
        _ => false,
    };

    partial void OnIsExtractedChanged(bool value) => ExtractCommand.NotifyCanExecuteChanged();

    partial void OnCreatedChanged(ProjectResultInfo? value) => ExtractCommand.NotifyCanExecuteChanged();

    /// <summary>As escolhas do assistente, no formato que o <c>vnf init</c> também monta.</summary>
    public ProjectInitOptions BuildOptions() => new(
        ProjectName,
        SelectedExe,
        SourceLanguages.Selected.Id,
        TargetLanguages.Selected.Id,
        EncodingStrategies.Selected.Id,
        SkipAi ? null : ProjectChoices.DefaultAi with { Model = Models.Selected.Id });

    private async Task CreateAsync()
    {
        IsBusy = true;
        try
        {
            var options = BuildOptions();
            var result = await Task.Run(() => ProjectInitializer.Initialize(ProjectDirectory!, GameFolder!, engines, options));
            Created = new ProjectResultInfo(result.Config.Name, result.Folder.Root);
            Step = WizardStep.Done;
        }
        catch (Exception e) when (IsExpected(e))
        {
            // O que o Core recusa (pasta do projeto dentro do jogo, já existe projeto) se resolve no passo do nome.
            ErrorMessage = $"{e.Message} Ajuste o nome ou a pasta do projeto.";
            Step = WizardStep.Names;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DescribeDetection(GameInspection found)
    {
        DetectionLines.Clear();
        var details = found.Detection.Details;
        var version = details.TryGetValue(ProjectInitializer.VersionDetail, out var ypf) && int.TryParse(ypf, out var v) ? v : (int?)null;
        DetectionLines.Add($"Engine: {ProjectLabels.Engine(found.Engine.Id, version)}");
        if (details.TryGetValue("scriptsArchive", out var archive))
        {
            var count = details.TryGetValue("scripts", out var n) ? $" ({n} scripts)" : string.Empty;
            DetectionLines.Add($"Arquivo de scripts: {archive}{count}");
        }

        if (details.TryGetValue(ProjectInitializer.ScriptKeyDetail, out var key))
        {
            DetectionLines.Add($"Chave XOR dos scripts: {key}");
        }

        if (details.TryGetValue(ProjectInitializer.WarningsDetail, out var warnings))
        {
            DetectionLines.Add($"Aviso: {warnings}");
        }
    }

    private static ChoiceOption EncodingOption(string id) => id switch
    {
        "fold" => new(id, "Remover acentos", "fold", "Funciona em qualquer jogo, mas perde todos os acentos. É o padrão."),
        "remap" => new(id, "Remapear glifos", "remap", "Boa qualidade, com fonte modificada. O build ainda não tem esse encoder."),
        "cp1252" => new(id, "CP1252", "cp1252", "Acentos completos, mas só funciona com o patch do executável."),
        _ => new(id, id),
    };

    private static ChoiceOption ModelOption(string id)
    {
        var price = ModelPricing.For(id);
        var value = string.Create(PtBr, $"US$ {price.Input:0.##} / {price.Output:0.##}");
        var description = id switch
        {
            "claude-opus-5-5" => "Padrão para tradução e revisão.",
            "claude-sonnet-5-5" => "Rascunho mais barato e rápido.",
            "claude-haiku-5-5" => "Menus, nomes e textos curtos.",
            "claude-fable-5-1" => "Cenas difíceis e revisão fina.",
            _ => string.Empty,
        };
        return new ChoiceOption(id, ProjectLabels.Model(id), $"{value} por milhão de tokens", description);
    }

    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    private static string FolderNameOf(string? path) =>
        path is null ? string.Empty : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private static bool IsExpected(Exception e) =>
        e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException;
}

/// <summary>Nome e pasta do projeto que o assistente acabou de criar.</summary>
public sealed record ProjectResultInfo(string Name, string Folder);
