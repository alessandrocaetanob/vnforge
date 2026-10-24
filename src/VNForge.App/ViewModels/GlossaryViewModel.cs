using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>As duas tabelas da página Glossário.</summary>
public enum GlossaryTab
{
    Terms,
    Characters,
}

/// <summary>
/// Página Glossário: as tabelas editáveis de <c>glossary.json</c> e <c>characters.json</c>, com busca e a contagem de linhas do projeto que usam cada uma.
/// Nada é gravado até <see cref="SaveCommand"/>; só o arquivo que mudou é reescrito, e um arquivo ilegível trava a edição para o salvamento não sobrescrevê-lo.
/// A busca só esconde linhas da tela: <see cref="Terms"/> e <see cref="Characters"/> seguem completos e são eles que vão para o arquivo.
/// </summary>
public sealed partial class GlossaryViewModel : ObservableObject
{
    private bool termsDirty;
    private bool charactersDirty;
    private bool loadFailed;
    private bool loading;
    private GlossaryUsage usage = GlossaryUsage.Empty;
    private bool usageLoaded;
    private int usageVersion;

    public GlossaryViewModel()
    {
        Terms.CollectionChanged += (_, _) =>
        {
            RefreshVisible();
            MarkTermsDirty();
        };
        Characters.CollectionChanged += (_, _) =>
        {
            RefreshVisible();
            MarkCharactersDirty();
        };
    }

    public ObservableCollection<GlossaryTermRow> Terms { get; } = [];

    public ObservableCollection<CharacterRow> Characters { get; } = [];

    /// <summary>Os termos que a tela mostra: <see cref="Terms"/> menos o que a <see cref="Search"/> esconde.</summary>
    public ObservableCollection<GlossaryTermRow> VisibleTerms { get; } = [];

    public ObservableCollection<CharacterRow> VisibleCharacters { get; } = [];

    public bool NoTerms => Terms.Count == 0;

    public bool NoCharacters => Characters.Count == 0;

    public bool NoVisibleTerms => VisibleTerms.Count == 0;

    public bool NoVisibleCharacters => VisibleCharacters.Count == 0;

    public int TermCount => Terms.Count;

    public int CharacterCount => Characters.Count;

    public string TermsSummary => Summarize(Terms.Count, "termo", "termos", Terms.Select(t => t.Uses), "no projeto");

    public string CharactersSummary => Summarize(Characters.Count, "personagem", "personagens", Characters.Select(c => c.Uses), "faladas");

    public string TermsEmptyText => HasSearch ? NoMatchText : "Nenhum termo ainda. Adicione o primeiro.";

    public string CharactersEmptyText => HasSearch ? NoMatchText : "Nenhum personagem ainda. O nome original é o falante extraído do jogo.";

    /// <summary>Termina quando a contagem de linhas do projeto foi lida (ou falhou); a tela não espera por ela.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Valores do combo de gênero, na ordem do enum.</summary>
    public IReadOnlyList<CharacterGender> Genders { get; } = Enum.GetValues<CharacterGender>();

    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }

    /// <summary>Texto da caixa de busca; filtra a tabela por original, tradução e observação (ou tom de fala).</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex), nameof(IsTermsTab), nameof(IsCharactersTab), nameof(AddLabel), nameof(SearchHint))]
    public partial GlossaryTab SelectedTab { get; set; }

    /// <summary>Índice da aba para o <c>TabControl</c>.</summary>
    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set => SelectedTab = (GlossaryTab)value;
    }

    public bool IsTermsTab => SelectedTab == GlossaryTab.Terms;

    public bool IsCharactersTab => SelectedTab == GlossaryTab.Characters;

    public string AddLabel => IsTermsTab ? "Adicionar termo" : "Adicionar personagem";

    public string SearchHint => IsTermsTab ? "Buscar termo, tradução ou observação" : "Buscar personagem";

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

    partial void OnSearchChanged(string value) => RefreshVisible();

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
            usage = GlossaryUsage.Empty;
            usageLoaded = false;
            Search = string.Empty;
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
            AddCommand.NotifyCanExecuteChanged();
        }

        Loading = LoadUsageAsync();
    }

    /// <summary>Lê o texto do projeto fora da thread da tela e põe a contagem de linhas em cada termo e personagem.</summary>
    private async Task LoadUsageAsync()
    {
        var version = ++usageVersion;
        var root = Project?.Root;
        var loaded = GlossaryUsage.Empty;
        if (root is not null)
        {
            try
            {
                loaded = await Task.Run(() => GlossaryUsage.From(ProjectFolder.Open(root).LoadAllText().Values.SelectMany(units => units)));
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                // Sem o texto não há o que contar; a página continua editável e as linhas mostram zero.
            }
        }

        if (version != usageVersion)
        {
            return;
        }

        usage = loaded;
        usageLoaded = true;
        foreach (var row in Terms)
        {
            ApplyUses(row);
        }

        foreach (var row in Characters)
        {
            ApplyUses(row);
        }

        NotifySummaries();
    }

    private void ApplyUses(UsageRow row) =>
        row.Uses = !usageLoaded ? null : row is CharacterRow ? usage.LinesSpokenBy(row.Key) : usage.LinesContaining(row.Key);

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

    /// <summary>Adiciona uma linha em branco na tabela da aba aberta.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Add()
    {
        if (IsTermsTab)
        {
            AddTerm();
        }
        else
        {
            AddCharacter();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddTerm()
    {
        ClearSearch();
        Terms.Add(Watch(new GlossaryTermRow(), MarkTermsDirty));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddCharacter()
    {
        ClearSearch();
        Characters.Add(Watch(new CharacterRow(), MarkCharactersDirty));
    }

    // A linha nova é vazia e a busca a esconderia.
    private void ClearSearch() => Search = string.Empty;

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
        where T : UsageRow
    {
        ApplyUses(row);
        row.PropertyChanged += (_, e) =>
        {
            // A contagem não é dado do arquivo: mexer nela não suja a página.
            if (e.PropertyName is nameof(UsageRow.Uses) or nameof(UsageRow.UsesLabel) or nameof(UsageRow.IsUnused))
            {
                NotifySummaries();
                return;
            }

            if (e.PropertyName == nameof(GlossaryTermRow.Source))
            {
                ApplyUses(row);
            }

            markDirty();
        };
        return row;
    }

    private bool HasSearch => !string.IsNullOrWhiteSpace(Search);

    private string NoMatchText => $"Nada corresponde a \"{Search.Trim()}\".";

    private bool Matches(params string[] fields) =>
        !HasSearch || fields.Any(f => f.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));

    private void RefreshVisible()
    {
        Replace(VisibleTerms, Terms.Where(t => Matches(t.Source, t.Target, t.Notes)));
        Replace(VisibleCharacters, Characters.Where(c => Matches(c.Source, c.Target, c.Tone)));
        OnPropertyChanged(nameof(NoTerms));
        OnPropertyChanged(nameof(NoCharacters));
        OnPropertyChanged(nameof(NoVisibleTerms));
        OnPropertyChanged(nameof(NoVisibleCharacters));
        OnPropertyChanged(nameof(TermCount));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(TermsEmptyText));
        OnPropertyChanged(nameof(CharactersEmptyText));
        NotifySummaries();
    }

    private void NotifySummaries()
    {
        OnPropertyChanged(nameof(TermsSummary));
        OnPropertyChanged(nameof(CharactersSummary));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string Summarize(int count, string one, string many, IEnumerable<int?> uses, string suffix)
    {
        var head = $"{count} {(count == 1 ? one : many)}";
        var known = uses.ToList();
        if (known.Count == 0 || known.Any(u => u is null))
        {
            return head;
        }

        var lines = known.Sum(u => u!.Value);
        return $"{head} · {ProjectLabels.Number(lines)} {(lines == 1 ? "linha" : "linhas")} {suffix}";
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
