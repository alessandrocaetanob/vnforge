using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>As duas tabelas da página Glossário.</summary>
public enum GlossaryTab
{
    Characters,
    Terms,
}

/// <summary>
/// Página Glossário: as tabelas editáveis de <c>glossary.json</c> e <c>characters.json</c>, com busca e a contagem de linhas do projeto que usam cada uma.
/// Com <see cref="AutosaveClock"/> definido, grava sozinha <see cref="SaveDelay"/> depois da última edição (e ao sair da página ou trocar de projeto); sem ele
/// só <see cref="SaveCommand"/> grava. Só o arquivo que mudou é reescrito, e um arquivo ilegível trava a edição para o salvamento não sobrescrevê-lo.
/// A busca só esconde linhas da tela: <see cref="Terms"/> e <see cref="Characters"/> seguem completos e são eles que vão para o arquivo.
/// </summary>
public sealed partial class GlossaryViewModel : ObservableObject
{
    /// <summary>Quanto a página espera sem edição antes de gravar.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private ITimer? saveTimer;
    private bool savedOnce;
    private bool termsDirty;
    private bool charactersDirty;
    private bool loadFailed;
    private bool loading;
    private GlossaryUsage usage = GlossaryUsage.Empty;
    private bool usageLoaded;
    private int usageVersion;
    private bool bulkUpdate;

    public GlossaryViewModel()
    {
        Terms.CollectionChanged += (_, _) =>
        {
            RefreshVisible();
            RefreshRepeatedTerms();
            MarkTermsDirty();
        };
        Characters.CollectionChanged += (_, _) =>
        {
            RefreshVisible();
            NotifyVoices();
            RefreshRepeatedTerms();
            MarkCharactersDirty();
        };
    }

    /// <summary>Relógio do salvamento automático; nulo desliga o automático (os testes e a galeria não gravam sozinhos).</summary>
    public TimeProvider? AutosaveClock { get; set; }

    /// <summary>Roda a gravação agendada na thread da interface; o padrão é o contexto de quem criou a página.</summary>
    public Action<Action> PostToUi { get; set; } = CaptureContext();

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

    public string CharactersEmptyText => HasSearch ? NoMatchText : "Nenhum personagem ainda. Peça sugestões pelas falas ou adicione o primeiro.";

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

    public string SearchHint => IsTermsTab ? "Buscar termo, tradução ou observação" : "Buscar por nome ou jeito de falar";

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string? NoticeMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsDirty { get; private set; }

    /// <summary>O que a página diz sobre o salvamento: nada antes da primeira gravação, "Salvando…" até gravar e "Salvo automaticamente" depois.</summary>
    public string SaveStatus => (SaveFailed, IsDirty, savedOnce) switch
    {
        (true, _, _) => "Não foi possível salvar",
        (_, true, _) => "Salvando…",
        (_, _, true) => "Salvo automaticamente",
        _ => string.Empty,
    };

    /// <summary>A última gravação falhou: o que está na tela ainda não está no disco.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    public partial bool SaveFailed { get; private set; }

    /// <summary>O projeto que a pessoa abriu enquanto este tinha alterações que não puderam ser salvas; a página espera a decisão dela.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingSwitch), nameof(DiscardLabel))]
    public partial ProjectSummary? PendingProject { get; private set; }

    public bool HasPendingSwitch => PendingProject is not null;

    public string DiscardLabel => PendingProject is null ? "Descartar alterações" : "Descartar e abrir o outro projeto";

    /// <summary>Falso sem projeto ou quando um dos arquivos não pôde ser lido: as tabelas ficam fechadas para edição.</summary>
    public bool CanEdit => Project is not null && !loadFailed;

    /// <summary>Os termos do glossário que aparecem no original, pelo estado atual da tabela (inclusive o ainda não salvo).</summary>
    public IReadOnlyList<GlossaryTerm> FindTerms(string? source) =>
        GlossaryMatcher.Find(source, Terms.Where(t => !t.IsBlank).Select(t => t.ToTerm()));

    // Trocar de projeto descarta as tabelas: o que ainda estava no ar do projeto antigo grava antes.
    partial void OnProjectChanging(ProjectSummary? value) => Flush();

    partial void OnProjectChanged(ProjectSummary? value) => Reload();

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(SaveStatus));

    partial void OnSearchChanged(string value) => RefreshVisible();

    /// <summary>Descarta o que não foi salvo e lê os dois arquivos de novo.</summary>
    public void Reload()
    {
        loading = true;
        CancelSaveTimer();
        try
        {
            Terms.Clear();
            Characters.Clear();
            ErrorMessage = null;
            NoticeMessage = null;
            loadFailed = false;
            usage = GlossaryUsage.Empty;
            usageLoaded = false;
            ResetSuggestions();
            savedOnce = false;
            SaveFailed = false;
            PendingProject = null;
            Search = string.Empty;
            if (Project is not null)
            {
                ReadFiles(ProjectFolder.Open(Project.Root));
            }
        }
        finally
        {
            loading = false;
            RefreshVisible();
            RefreshRepeatedTerms();
            NotifyVoices();
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
        var loaded = ProjectScan.Empty;
        if (root is not null)
        {
            try
            {
                loaded = await Task.Run(() => ProjectScan.From(ProjectFolder.Open(root).LoadAllText()), CancellationToken.None);
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

        usage = loaded.Usage;
        scan = loaded;
        usageLoaded = true;
        AddGameCharacters();
        bulkUpdate = true;
        foreach (var row in Terms)
        {
            ApplyUses(row);
        }

        foreach (var row in Characters)
        {
            ApplyUses(row);
        }

        bulkUpdate = false;
        NotifySummaries();
        RefreshRepeatedTerms();
        NotifyVoices();
    }

    /// <summary>
    /// Parte da lista do jogo (#426): os nomes das unidades <c>name</c> que ainda não são personagem entram na tabela com o original exato, e o personagem
    /// sem tradução recebe a que a unidade já tem. Não é edição da pessoa, então não suja a página; as linhas novas vão ao arquivo na próxima gravação.
    /// </summary>
    private void AddGameCharacters()
    {
        if (!CanEdit || scan.GameNames.Count == 0)
        {
            return;
        }

        var names = new CharacterBook(scan.GameNames);
        loading = true;
        try
        {
            foreach (var row in Characters.Where(c => string.IsNullOrWhiteSpace(c.Target)))
            {
                if (names.Find(row.Source) is { Target.Length: > 0 } name)
                {
                    row.Target = name.Target;
                }
            }

            foreach (var entry in GameCharacters.Missing(scan.GameNames, Characters.Select(c => new CharacterEntry(c.Source, c.Target))))
            {
                Characters.Add(Watch(new CharacterRow(entry.Source, entry.Target), MarkCharactersDirty));
            }
        }
        finally
        {
            loading = false;
        }

        RefreshVisible();
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
            Debug.WriteLine(e);
            ErrorMessage = $"Um dos arquivos do glossário está inválido: {e.Reason} Corrija-o para editar aqui.";
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

    /// <summary>
    /// Abre a aba Personagens com <paramref name="source"/>, o nome exatamente como está no texto extraído (sem aparar nem normalizar), numa linha nova no alto da tabela
    /// para a pessoa só preencher a tradução. Se o nome já está cadastrado, devolve a linha dele em vez de duplicar. Nulo quando a tabela está fechada para edição.
    /// </summary>
    public CharacterRow? RegisterCharacter(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!CanEdit)
        {
            return null;
        }

        SelectedTab = GlossaryTab.Characters;
        ClearSearch();
        var existing = Characters.FirstOrDefault(c => string.Equals(c.Source.Trim(), source.Trim(), StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var row = Watch(new CharacterRow(source), MarkCharactersDirty);
        Characters.Insert(0, row);
        return row;
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

    /// <summary>Pedido da linha de um personagem: o modo revisão com as falas dele em todas as cenas. O argumento é o nome como o texto o guarda.</summary>
    public event EventHandler<string>? ReviewCharacterRequested;

    [RelayCommand]
    private void ReviewCharacter(CharacterRow? row)
    {
        if (row is not null && !string.IsNullOrWhiteSpace(row.Source))
        {
            ReviewCharacterRequested?.Invoke(this, row.Source.Trim());
        }
    }

    private bool CanSave() => IsDirty && CanEdit;

    /// <summary>Disparado depois de uma gravação que chegou ao disco: o Editor refaz os avisos do QA com o glossário e os personagens novos.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Grava os dois arquivos. Linha com o nome vazio ou repetido não trava a gravação das outras: fica de fora, marcada na tela, até a pessoa corrigi-la.
    /// Se a gravação falha, as edições continuam na tela e <see cref="SaveFailed"/> fica verdadeiro.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var folder = ProjectFolder.Open(Project!.Root);
        ErrorMessage = null;
        NoticeMessage = null;
        var terms = ValidRows(Terms, "Falta o termo em japonês.").Select(t => t.ToTerm()).ToList();
        var characters = ValidRows(Characters, "Falta o nome do personagem no jogo.").Select(c => c.ToEntry()).ToList();
        NotifyProblems();
        try
        {
            if (termsDirty)
            {
                // Só linhas em branco e arquivo ausente: não cria um glossary.json vazio no projeto.
                if (terms.Count > 0 || File.Exists(folder.GlossaryPath))
                {
                    folder.SaveGlossary(terms);
                }

                termsDirty = false;
            }

            if (charactersDirty)
            {
                if (characters.Count > 0 || File.Exists(folder.CharactersPath))
                {
                    folder.SaveCharacters(characters);
                    ShareNamesWithLines(folder, characters);
                }

                charactersDirty = false;
            }
        }
        catch (GlossaryFileException e)
        {
            Debug.WriteLine(e);
            FailSave($"{e.Reason} Nada foi gravado neste arquivo.");
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TextFileException)
        {
            Debug.WriteLine(e);
            FailSave("Não foi possível salvar. Confira se a pasta do projeto está acessível e se há espaço no disco; o que você escreveu continua na tela.");
            return;
        }

        SaveFailed = false;
        savedOnce = true;
        IsDirty = false;
        OnPropertyChanged(nameof(SaveStatus));
        Saved?.Invoke(this, EventArgs.Empty);

        // Um salvamento que passou depois da falha libera o projeto que esperava: não há mais o que decidir.
        if (PendingProject is { } next)
        {
            PendingProject = null;
            Project = next;
        }
    }

    /// <summary>Disparado quando salvar um personagem mudou a tradução de linhas <c>name</c> dos scripts: o Editor, que as tem na memória, relê o projeto.</summary>
    public event EventHandler? LinesChanged;

    /// <summary>Um nome do jogo tem uma só tradução: a do personagem vai para as unidades <c>name</c> do mesmo original (#426).</summary>
    private void ShareNamesWithLines(ProjectFolder folder, IReadOnlyList<CharacterEntry> characters)
    {
        // Antes de a leitura do texto acabar não se sabe em quais scripts há nomes: relê todos. Depois, só os que têm.
        IReadOnlyList<string>? scripts = usageLoaded ? scan.NameScripts : null;
        if (scripts is { Count: 0 })
        {
            return;
        }

        if (CharacterNameSync.ApplyToUnits(folder, new LineEditor(folder, TimeProvider.System), characters, scripts) > 0)
        {
            LinesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FailSave(string message)
    {
        ErrorMessage = message;
        SaveFailed = true;
    }

    /// <summary>As linhas que podem ir para o arquivo, na ordem da tabela. As incompletas e as repetidas (da segunda em diante) recebem o <see cref="UsageRow.Problem"/>.</summary>
    private static List<T> ValidRows<T>(IEnumerable<T> rows, string missing)
        where T : UsageRow
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var valid = new List<T>();
        foreach (var row in rows)
        {
            var key = row.Key.Trim();
            row.Problem = ProblemOf(row, key, seen, missing);
            if (!row.IsBlank && row.Problem is null)
            {
                seen.Add(key);
                valid.Add(row);
            }
        }

        return valid;
    }

    private static string? ProblemOf(UsageRow row, string key, HashSet<string> seen, string missing)
    {
        if (row.IsBlank)
        {
            return null;
        }

        if (key.Length == 0)
        {
            return missing;
        }

        return seen.Contains(key) ? "Já existe outra linha com este nome." : null;
    }

    /// <summary>Quantas linhas ficaram de fora do arquivo por nome vazio ou repetido.</summary>
    public int ProblemCount => Terms.Count(t => t.HasProblem) + Characters.Count(c => c.HasProblem);

    public bool HasProblems => ProblemCount > 0;

    public string ProblemsText => ProblemCount switch
    {
        0 => string.Empty,
        1 => "1 linha ainda não foi salva porque falta o nome ou ele se repete. As outras já foram. Corrija a linha marcada.",
        var n => $"{ProjectLabels.Number(n)} linhas ainda não foram salvas porque falta o nome ou ele se repete. As outras já foram. Corrija as linhas marcadas.",
    };

    private void NotifyProblems()
    {
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsText));
    }

    private T Watch<T>(T row, Action markDirty)
        where T : UsageRow
    {
        ApplyUses(row);
        row.PropertyChanged += (_, e) =>
        {
            // A contagem não é dado do arquivo: mexer nela não suja a página.
            if (e.PropertyName is nameof(UsageRow.Uses) or nameof(UsageRow.UsesLabel) or nameof(UsageRow.IsUnused) or nameof(CharacterRow.NeedsGender) or nameof(RepeatedTermRow.HasSuggestion) or nameof(UsageRow.Problem) or nameof(UsageRow.HasProblem)
                or nameof(CharacterRow.SuggestedGender) or nameof(CharacterRow.SuggestedGenderLabel) or nameof(CharacterRow.SuggestedTone) or nameof(CharacterRow.TonePlaceholder))
            {
                if (e.PropertyName == nameof(CharacterRow.HasSuggestion) && row is CharacterRow)
                {
                    NotifyVoices();
                }

                NotifySummaries();
                return;
            }

            if (e.PropertyName == nameof(GlossaryTermRow.Source))
            {
                ApplyUses(row);
            }

            if (row is CharacterRow)
            {
                NotifyVoices();
            }

            if (e.PropertyName == nameof(GlossaryTermRow.Source))
            {
                RefreshRepeatedTerms();
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
        if (loading)
        {
            return;
        }

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
        if (bulkUpdate)
        {
            return;
        }

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
        var head = $"{ProjectLabels.Number(count)} {(count == 1 ? one : many)}";
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
        ScheduleSave();
        NotifyProblems();
    }

    private void ScheduleSave()
    {
        if (AutosaveClock is not { } clock || !CanEdit)
        {
            return;
        }

        if (saveTimer is null)
        {
            saveTimer = clock.CreateTimer(_ => PostToUi(() => Flush()), null, SaveDelay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelSaveTimer() => saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Grava já o que estiver pendente, sem esperar o <see cref="SaveDelay"/>: sair da página, trocar de projeto e fechar a janela chamam isto.
    /// Devolve falso quando a gravação falhou e há edições que só existem na tela.
    /// </summary>
    public bool Flush()
    {
        CancelSaveTimer();
        if (CanSave())
        {
            Save();
        }

        return !(IsDirty && CanEdit);
    }

    /// <summary>
    /// Abre outro projeto na página. Antes grava o que está pendente; se não conseguir, o projeto novo fica em <see cref="PendingProject"/> e as edições
    /// continuam na tela até a pessoa tentar salvar de novo ou descartar. Devolve verdadeiro quando o projeto foi aberto.
    /// </summary>
    public bool Open(ProjectSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (Project?.Root != summary.Root && !Flush())
        {
            PendingProject = summary;
            return false;
        }

        Project = summary;
        return true;
    }

    /// <summary>Tenta gravar de novo; se der certo e havia um projeto à espera, abre-o.</summary>
    [RelayCommand]
    private void RetrySave()
    {
        // Quando a gravação passa, Save já abre o projeto que esperava.
        Flush();
    }

    /// <summary>Joga fora o que não foi salvo; se havia um projeto à espera, abre-o, senão relê os arquivos do projeto atual.</summary>
    [RelayCommand]
    private void DiscardChanges()
    {
        CancelSaveTimer();
        termsDirty = false;
        charactersDirty = false;
        IsDirty = false;
        var next = PendingProject;
        PendingProject = null;
        if (next is not null)
        {
            Project = next;
        }
        else
        {
            Reload();
        }
    }

    private static Action<Action> CaptureContext()
    {
        var context = SynchronizationContext.Current;
        return context is null ? action => action() : action => context.Post(_ => action(), null);
    }
}
