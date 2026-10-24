using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// Página Glossário: as tabelas editáveis de <c>glossary.json</c> e <c>characters.json</c>. Nada é gravado até <see cref="SaveCommand"/>;
/// só o arquivo que mudou é reescrito, e um arquivo ilegível trava a edição para o salvamento não sobrescrevê-lo.
/// </summary>
public sealed partial class GlossaryViewModel : ObservableObject
{
    private bool termsDirty;
    private bool charactersDirty;
    private bool loadFailed;
    private bool loading;

    public GlossaryViewModel()
    {
        Terms.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(NoTerms));
            MarkTermsDirty();
        };
        Characters.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(NoCharacters));
            MarkCharactersDirty();
        };
    }

    public ObservableCollection<GlossaryTermRow> Terms { get; } = [];

    public ObservableCollection<CharacterRow> Characters { get; } = [];

    public bool NoTerms => Terms.Count == 0;

    public bool NoCharacters => Characters.Count == 0;

    /// <summary>Valores do combo de gênero, na ordem do enum.</summary>
    public IReadOnlyList<CharacterGender> Genders { get; } = Enum.GetValues<CharacterGender>();

    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string? NoticeMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsDirty { get; private set; }

    /// <summary>Falso sem projeto ou quando um dos arquivos não pôde ser lido: as tabelas ficam fechadas para edição.</summary>
    public bool CanEdit => Project is not null && !loadFailed;

    /// <summary>Os termos do glossário que aparecem no original, pelo estado atual da tabela (inclusive o ainda não salvo).</summary>
    public IReadOnlyList<GlossaryTerm> FindTerms(string? source) =>
        GlossaryMatcher.Find(source, Terms.Where(t => !t.IsBlank).Select(t => t.ToTerm()));

    partial void OnProjectChanged(ProjectSummary? value) => Reload();

    /// <summary>Descarta o que não foi salvo e lê os dois arquivos de novo.</summary>
    public void Reload()
    {
        loading = true;
        try
        {
            Terms.Clear();
            Characters.Clear();
            ErrorMessage = null;
            NoticeMessage = null;
            loadFailed = false;
            if (Project is not null)
            {
                ReadFiles(ProjectFolder.Open(Project.Root));
            }
        }
        finally
        {
            loading = false;
            termsDirty = false;
            charactersDirty = false;
            IsDirty = false;
            OnPropertyChanged(nameof(CanEdit));
            AddTermCommand.NotifyCanExecuteChanged();
            AddCharacterCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private void ReadFiles(ProjectFolder folder)
    {
        try
        {
            foreach (var t in folder.LoadGlossary())
            {
                Terms.Add(Watch(new GlossaryTermRow(t.Source, t.Target, t.Notes), MarkTermsDirty));
            }

            foreach (var c in folder.LoadCharacters())
            {
                Characters.Add(Watch(new CharacterRow(c.Source, c.Target, c.Gender, c.Tone), MarkCharactersDirty));
            }
        }
        catch (GlossaryFileException e)
        {
            Terms.Clear();
            Characters.Clear();
            loadFailed = true;
            ErrorMessage = $"{e.Message} Corrija o arquivo para editar aqui.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddTerm() => Terms.Add(Watch(new GlossaryTermRow(), MarkTermsDirty));

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddCharacter() => Characters.Add(Watch(new CharacterRow(), MarkCharactersDirty));

    [RelayCommand]
    private void RemoveTerm(GlossaryTermRow? row)
    {
        if (row is not null)
        {
            Terms.Remove(row);
        }
    }

    [RelayCommand]
    private void RemoveCharacter(CharacterRow? row)
    {
        if (row is not null)
        {
            Characters.Remove(row);
        }
    }

    private bool CanSave() => IsDirty && CanEdit;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var folder = ProjectFolder.Open(Project!.Root);
        ErrorMessage = null;
        NoticeMessage = null;
        try
        {
            var terms = Terms.Where(t => !t.IsBlank).Select(t => t.ToTerm()).ToList();
            if (termsDirty)
            {
                // Só linhas em branco e arquivo ausente: não cria um glossary.json vazio no projeto.
                if (terms.Count > 0 || File.Exists(folder.GlossaryPath))
                {
                    folder.SaveGlossary(terms);
                }

                termsDirty = false;
            }

            var characters = Characters.Where(c => !c.IsBlank).Select(c => c.ToEntry()).ToList();
            if (charactersDirty)
            {
                if (characters.Count > 0 || File.Exists(folder.CharactersPath))
                {
                    folder.SaveCharacters(characters);
                }

                charactersDirty = false;
            }
        }
        catch (GlossaryFileException e)
        {
            ErrorMessage = $"{e.Reason} Nada foi gravado neste arquivo.";
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível gravar: {e.Message}";
            return;
        }

        IsDirty = false;
        NoticeMessage = "Alterações salvas.";
    }

    private T Watch<T>(T row, Action markDirty)
        where T : ObservableObject
    {
        row.PropertyChanged += (_, _) => markDirty();
        return row;
    }

    private void MarkTermsDirty()
    {
        if (!loading)
        {
            termsDirty = true;
            Touch();
        }
    }

    private void MarkCharactersDirty()
    {
        if (!loading)
        {
            charactersDirty = true;
            Touch();
        }
    }

    private void Touch()
    {
        NoticeMessage = null;
        IsDirty = true;
    }
}
