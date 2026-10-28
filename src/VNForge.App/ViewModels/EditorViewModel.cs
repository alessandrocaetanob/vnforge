using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Uma gravação de linha que mudou a tradução ou o status: a unidade de antes e a de depois, para o aviso "Desfazer".</summary>
public sealed record LineSavedEventArgs(string? ProjectRoot, string ScriptKey, int Number, TextUnit Previous, TextUnit Saved)
{
    /// <summary>As linhas de um "Usar em todos" desfeito de uma vez; vazio numa gravação de uma linha só.</summary>
    public IReadOnlyList<LineSavedEventArgs> Batch { get; init; } = [];

    public bool IsBatch => Batch.Count > 0;

    public bool IsApproval => !IsBatch && Saved.Status == TranslationStatus.Approved && Previous.Status != TranslationStatus.Approved;
}

/// <summary>Uma opção do seletor de personagem. <see cref="Value"/> é o <see cref="LineFilter.Speaker"/>: nulo para todos, <see cref="LineFilter.Narration"/> para a narração.</summary>
public sealed record SpeakerOption(string Label, string? Value);

/// <summary>
/// Página do Editor: lista de scripts com progresso, grade de linhas filtrável, a coluna do detalhe e o painel de busca e substituição.
/// A grade recebe <see cref="Lines"/> trocada de uma vez a cada filtro; mexer numa coleção observável de dezenas de milhares de linhas travaria a tela.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject, ILineNavigator
{
    private const string AllSpeakersLabel = "Todos os personagens";

    private readonly Func<string, ProjectLines> loader;
    private readonly FilterChipViewModel[] chips;
    private readonly TimeProvider clock;
    private readonly Action<Action>? postToUi;
    private readonly ILineSuggester suggester;
    private readonly Func<string, EditorContext> contextLoader;
    private readonly Func<string, string?> scriptLabel;
    private readonly Dictionary<string, TextUnit> savedUnits = new(StringComparer.Ordinal);
    private readonly FormattingProfileCache profiles = new();
    private readonly SpeakerMarksCache speakerMarks = new();
    private SpeakerNaming? speakerNaming;
    private LineEditor? lineEditor;
    private EditorContext context = EditorContext.Empty;
    private ProjectLines? data;
    private LineEntry[] entries = [];
    private LineViewModel[] scriptLines = [];
    private LineStatusFilter statusFilter = LineStatusFilter.All;
    private int loadVersion;
    private string? loadedRoot;
    private bool applying;
    private bool revertingSelection;
    private bool discardingSelection;

    /// <param name="loader">Lê as linhas do projeto; o padrão lê <c>text/*.json</c>.</param>
    /// <param name="clock">Relógio do painel de detalhe (debounce da gravação e horário da origem <c>manual</c>); o padrão é o do sistema.</param>
    /// <param name="postToUi">Como o painel executa a gravação agendada na thread da tela; nulo usa o <see cref="SynchronizationContext"/> da thread que o criou. Os testes passam uma chamada direta.</param>
    /// <param name="suggester">Pede a sugestão da IA do painel (Ctrl+Espaço); o padrão usa o provedor falso, sem rede.</param>
    /// <param name="contextLoader">Lê os personagens e o glossário do projeto; o padrão lê <c>characters.json</c> e <c>glossary.json</c>.</param>
    /// <param name="scriptLabel">Dá o nome da cena pela chave dela; sem ele (ou nulo) vale o nome do <c>scripts.json</c> e, sem esse também, "Cena N".</param>
    /// <param name="search">O painel de busca e substituição; o padrão cria o <see cref="SearchReplaceViewModel"/> do projeto.</param>
    /// <param name="layoutMemory">Onde o Editor lembra o arranjo escolhido (a coluna das cenas recolhida); nulo vale só até o app fechar.</param>
    public EditorViewModel(
        Func<string, ProjectLines>? loader = null,
        TimeProvider? clock = null,
        Action<Action>? postToUi = null,
        ILineSuggester? suggester = null,
        Func<string, EditorContext>? contextLoader = null,
        Func<string, string?>? scriptLabel = null,
        SearchReplaceViewModel? search = null,
        EditorLayoutMemory? layoutMemory = null)
    {
        RestoreLayout(layoutMemory);
        this.contextLoader = contextLoader ?? EditorContext.Read;
        this.scriptLabel = scriptLabel ?? (_ => null);
        Search = search ?? new SearchReplaceViewModel(clock);
        Search.FlushPendingEdits = FlushPendingEdits;
        Search.Applied += (_, _) => Reload();
        Search.ResultOpened += (_, row) => SearchNavigation = OpenSearchResultAsync(row);
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchReplaceViewModel.IsReplaceVisible))
            {
                OnPropertyChanged(nameof(IsReplaceVisible));
            }
        };
        this.suggester = suggester ?? new LineSuggester(() => null);
        this.clock = clock ?? TimeProvider.System;
        this.postToUi = postToUi;
        this.loader = loader ?? (root => ProjectLines.Read(ProjectFolder.Open(root)));
        chips =
        [
            new FilterChipViewModel(LineStatusFilter.All, "Todas") { IsSelected = true },
            new FilterChipViewModel(LineStatusFilter.Pending, "Em japonês"),
            new FilterChipViewModel(LineStatusFilter.Machine, "Para revisar"),
            new FilterChipViewModel(LineStatusFilter.Warnings, "Com aviso"),
            new FilterChipViewModel(LineStatusFilter.Keep, "Não traduzir"),
            new FilterChipViewModel(LineStatusFilter.ChangedSinceTest, "Mudou desde o teste"),
        ];
        filters = [.. chips.Where(c => c.Filter != LineStatusFilter.ChangedSinceTest)];
        SelectedSpeaker = Speakers[0];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    public partial ProjectSummary? Project { get; set; }

    /// <summary>A carga das linhas em andamento (ou a última); os testes a aguardam.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Quem encurta a fala a pedido do aviso "Não cabe na caixa do jogo"; nulo usa o próprio sugestor quando ele sabe encurtar.</summary>
    public ILineShortener? Shortener { get; init; }

    /// <summary>Disparado a cada gravação que mudou a tradução ou o status de uma linha (o aviso "Linha 12 gravada").</summary>
    public event EventHandler<LineSavedEventArgs>? LineSaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(ShowEmptyState))]
    public partial bool IsLoading { get; set; }

    /// <summary>Falha ao ler as linhas: o fato e a consequência, em português.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(ShowEmptyState))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Cria o gravador de linhas do projeto aberto; os testes trocam por um que falha. O padrão grava em <c>text/*.json</c>.</summary>
    public Func<string, LineEditor?>? EditorFactory { get; init; }

    /// <summary>O "Entender o japonês" do inspetor; nulo deixa o botão desligado.</summary>
    public ILineExplanations? Explanations { get; init; }

    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = [];

    /// <summary>
    /// Os scripts que passam por <see cref="ScriptFilter"/>. A lista da tela tem todos os itens e esconde os que não passam, para o script aberto
    /// continuar selecionado mesmo quando o filtro o esconde.
    /// </summary>
    public IEnumerable<ScriptItemViewModel> VisibleScripts => Scripts.Where(s => s.IsShown);

    /// <summary>O texto do campo "Filtrar scripts": casa a chave ou o nome do capítulo.</summary>
    [ObservableProperty]
    public partial string ScriptFilter { get; set; } = string.Empty;

    /// <summary>A chave do script que a IA está traduzindo; o item dele na lista mostra o ícone girando. Nulo sem tarefa.</summary>
    [ObservableProperty]
    public partial string? RunningScriptKey { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckFileCommand), nameof(TranslatePendingCommand))]
    public partial ScriptItemViewModel? SelectedScript { get; set; }

    public IReadOnlyList<FilterChipViewModel> Filters => filters;

    [ObservableProperty]
    public partial IReadOnlyList<SpeakerOption> Speakers { get; private set; } = [new SpeakerOption(AllSpeakersLabel, null)];

    [ObservableProperty]
    public partial SpeakerOption? SelectedSpeaker { get; set; }

    /// <summary>As linhas visíveis. É trocada por inteiro a cada filtro ou script; nunca recebe <c>Add</c> nem <c>Remove</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(LinesSummary))]
    public partial IReadOnlyList<LineViewModel> Lines { get; private set; } = [];

    [ObservableProperty]
    public partial LineViewModel? SelectedLine { get; set; }

    /// <summary>
    /// Painel de detalhe da <see cref="SelectedLine"/>, recriado a cada troca de linha. Nulo sem linha selecionada ou quando a pasta do projeto não abre
    /// (não há onde gravar). Trocar de linha grava antes o que estiver pendente no painel.
    /// </summary>
    [ObservableProperty]
    public partial LineDetailViewModel? Detail { get; private set; }

    /// <summary>Quando o painel gravou uma linha pela última vez nesta sessão; nulo antes da primeira gravação.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? LastSavedAt { get; private set; }

    /// <summary>Quantas ocorrências do QA o projeto tem, somadas em todas as linhas; acompanha as edições do painel.</summary>
    [ObservableProperty]
    public partial int IssueCount { get; private set; }

    /// <summary>Quantas dessas ocorrências são erros, os que travam o teste no jogo; os avisos não entram.</summary>
    [ObservableProperty]
    public partial int ErrorCount { get; private set; }

    public string ProjectName => Project?.Name ?? string.Empty;

    /// <summary>A sobrancelha do cabeçalho: "Capítulo 1 · cena 13 de 45", ou "Cena 13 de 45" fora de um capítulo da história. Nunca a chave do script.</summary>
    public string Breadcrumb
    {
        get
        {
            if (SelectedScript is not { } script)
            {
                return string.Empty;
            }

            if (script.Chapter is not { } chapter)
            {
                return $"Cena {ProjectLabels.Number(script.Position)} de {ProjectLabels.Number(Scripts.Count)}";
            }

            var scene = $"cena {ProjectLabels.Number(script.Position)} de {ProjectLabels.Number(chapter.Scripts.Count)}";
            return chapter.Number is { } number ? $"Capítulo {number} · {scene}" : char.ToUpperInvariant(scene[0]) + scene[1..];
        }
    }

    /// <summary>O título do cabeçalho: o nome da cena, ou "Cena N" quando o projeto não guarda o nome. Nunca a chave do script.</summary>
    public string ScriptTitle => SelectedScript?.Name ?? string.Empty;

    /// <summary>Linhas do script aberto que ainda não têm tradução.</summary>
    public int PendingLines { get; private set; }

    public string ScriptsCount => ProjectLabels.Number(Scripts.Count);

    public StatusDistribution? Distribution => SelectedScript?.Distribution;

    public string DistributionDescription => SelectedScript?.DistributionDescription ?? string.Empty;

    /// <summary>Total da cena; com filtro ativo, "12 de 890 linhas nesta cena".</summary>
    public string LinesSummary
    {
        get
        {
            var total = entries.Length;
            var noun = total == 1 ? "linha" : "linhas";
            return Lines.Count == total
                ? $"{ProjectLabels.Number(total)} {noun} nesta cena"
                : $"{ProjectLabels.Number(Lines.Count)} de {ProjectLabels.Number(total)} {noun} nesta cena";
        }
    }

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>A grade está vazia de verdade (não está lendo): o estado vazio aparece; lendo, é o esqueleto.</summary>
    public bool ShowEmptyState => IsEmpty && !IsLoading;

    /// <summary>Há um filtro de status ativo; "Ver todas as linhas" tira o filtro quando ele esvazia a grade.</summary>
    public bool HasStatusFilter => statusFilter != LineStatusFilter.All;

    public string? EmptyActionText => HasStatusFilter ? "Ver todas as linhas" : null;

    public string EmptyMessage =>
        IsLoading ? "Lendo as linhas do projeto..."
        : ErrorMessage is not null ? "Não há linhas para mostrar."
        : Scripts.Count == 0 ? "Este projeto ainda não tem texto extraído. Rode vnf extract."
        : "Nenhuma linha com este filtro.";

    /// <summary>Lê as linhas do <see cref="Project"/> fora da thread da tela. Uma carga mais nova descarta o resultado de uma mais antiga.</summary>
    public async Task LoadAsync()
    {
        var root = Project?.Root;
        var version = ++loadVersion;
        ErrorMessage = null;
        if (!string.Equals(root, loadedRoot, StringComparison.Ordinal))
        {
            // A hora da última gravação é do projeto aberto; só um Reload do mesmo projeto a mantém.
            LastSavedAt = null;
            loadedRoot = root;
            closedChapters.Clear();
            scriptNames = ScriptNames.Empty;
            CancelNewChapter();
        }

        if (root is null)
        {
            IsLoading = false;
            Apply(null);
            return;
        }

        // As linhas do projeto anterior não podem ficar na tela como se fossem do novo enquanto ele carrega.
        Apply(null);
        lineEditor = null;
        context = EditorContext.Empty;
        savedUnits.Clear();
        IsLoading = true;
        ProjectLines? loaded = null;
        var loadedContext = EditorContext.Empty;
        NamesRead? loadedNames = null;
        string? error = null;
        try
        {
            (loaded, loadedContext, loadedNames) = await Task.Run<(ProjectLines, EditorContext, NamesRead)>(() => (loader(root), contextLoader(root), ReadNames(root)));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            error = $"Não foi possível ler as linhas do projeto: {e.Message.TrimEnd('.', ' ')}. A grade fica vazia.";
        }

        if (version != loadVersion)
        {
            return;
        }

        IsLoading = false;
        ErrorMessage = error;
        lineEditor = CreateLineEditor(loaded, root);
        context = loadedContext;
        scriptNames = loadedNames?.Names ?? scriptNames;
        if (loadedNames?.Error is { } namesError)
        {
            ChapterMessage = namesError;
        }

        Apply(loaded);
    }

    /// <summary>
    /// Relê as linhas do projeto atual, p. ex. depois de a Conferência corrigir textos fora do Editor, e volta ao script e à linha que estavam
    /// selecionados (se ainda existirem).
    /// </summary>
    public void Reload()
    {
        var scriptKey = SelectedScript?.Key;
        var unitId = SelectedLine?.Id;
        var load = LoadAsync();
        var version = loadVersion;
        Loading = RestoreSelectionAsync(load, version, scriptKey, unitId);
    }

    private async Task RestoreSelectionAsync(Task load, int version, string? scriptKey, string? unitId)
    {
        await load;

        // Uma carga mais nova (outro projeto, outro Reload) cuida da própria seleção.
        if (version == loadVersion && scriptKey is not null && unitId is not null)
        {
            SelectLine(scriptKey, unitId);
        }
    }

    /// <summary>
    /// Seleciona a linha <paramref name="unitId"/> do script <paramref name="scriptKey"/>, trocando de script e tirando os filtros que a escondem.
    /// Espera a carga em andamento. Devolve falso, sem mexer na seleção, quando o script ou a linha não existem.
    /// </summary>
    public async Task<bool> GoToLineAsync(string scriptKey, string unitId)
    {
        await Loading;
        return SelectLine(scriptKey, unitId);
    }

    /// <summary>
    /// Procura <paramref name="text"/>, sem diferenciar maiúsculas (nem acentos, na tradução), no original e na tradução de todos os scripts, na ordem do projeto.
    /// Para ao chegar em <paramref name="limit"/> linhas; texto vazio não acha nada.
    /// </summary>
    public IReadOnlyList<LineEntry> FindLines(string text, int limit)
    {
        var found = new List<LineEntry>();
        if (data is null || string.IsNullOrWhiteSpace(text) || limit <= 0)
        {
            return found;
        }

        var filter = new LineFilter(Query: text);
        foreach (var script in data.Scripts)
        {
            foreach (var entry in data.LinesOf(script.Key).Where(filter.Matches))
            {
                found.Add(entry);
                if (found.Count >= limit)
                {
                    return found;
                }
            }
        }

        return found;
    }

    /// <summary>Deixa só as linhas pendentes na grade, como o chip "Em japonês".</summary>
    public void ShowPendingLines() => SelectFilter(chips.First(c => c.Filter == LineStatusFilter.Pending));

    private bool SelectLine(string scriptKey, string unitId)
    {
        var script = Scripts.FirstOrDefault(s => string.Equals(s.Key, scriptKey, StringComparison.Ordinal));
        var entry = data?.LinesOf(scriptKey).FirstOrDefault(l => string.Equals(l.Unit.Id, unitId, StringComparison.Ordinal));
        if (script is null || entry is null)
        {
            return false;
        }

        if (!ReferenceEquals(SelectedScript, script))
        {
            SelectedScript = script;
        }

        var target = scriptLines[entry.Number - 1];
        if (IndexOf(target) < 0)
        {
            SelectFilter(chips[0]);
            SelectedSpeaker = Speakers[0];
        }

        SelectedLine = target;
        return ReferenceEquals(SelectedLine, target);
    }

    partial void OnScriptFilterChanged(string value)
    {
        ApplyScriptFilter();
        OnPropertyChanged(nameof(VisibleScripts));
    }

    partial void OnRunningScriptKeyChanged(string? value)
    {
        foreach (var script in Scripts)
        {
            script.IsRunning = value is not null && script.Key == value;
        }
    }

    private void ApplyScriptFilter()
    {
        foreach (var script in Scripts)
        {
            script.IsShown = script.Matches(ScriptFilter);
        }

        RefreshChapterList();
    }

    /// <summary>Disparado por "Conferir arquivo": a janela abre Conferir a tradução. Leva a chave do script aberto.</summary>
    public event EventHandler<string>? CheckRequested;

    /// <summary>Disparado por "Traduzir com IA": a janela com as três perguntas abre. Leva as cenas marcadas na lista (Ctrl ou Shift) ou, sem marcação múltipla, a chave do script aberto.</summary>
    public event EventHandler<IReadOnlyList<string>>? TranslateRequested;

    /// <summary>Os scripts marcados na lista de cenas; a lista os mantém por <c>SelectedItems</c>.</summary>
    public ObservableCollection<ScriptItemViewModel> ChosenScripts { get; } = [];

    [RelayCommand(CanExecute = nameof(HasScript))]
    private void CheckFile() => CheckRequested?.Invoke(this, SelectedScript!.Key);

    [RelayCommand(CanExecute = nameof(CanTranslatePending))]
    private void TranslatePending()
    {
        // Uma cena escondida pelo filtro continua marcada na lista, mas a pessoa não a vê: fica fora do pedido.
        var marked = ChosenScripts.ToHashSet();
        var chosen = Scripts.Where(s => s.IsShown && marked.Contains(s)).Select(s => s.Key).ToList();
        TranslateRequested?.Invoke(this, chosen.Count > 0 ? chosen : [SelectedScript!.Key]);
    }

    private bool HasScript() => SelectedScript is not null;

    // A janela oferece também o capítulo e o jogo inteiro, então abre mesmo quando a cena aberta não tem pendentes.
    private bool CanTranslatePending() => SelectedScript is not null;

    /// <summary>O painel de busca e substituição do projeto, que abre dentro do Editor.</summary>
    public SearchReplaceViewModel Search { get; }

    /// <summary>O painel de busca está aberto sobre a grade.</summary>
    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    /// <summary>O campo "Substituir por" e os botões de troca estão visíveis (Ctrl+H); sem eles o painel só busca.</summary>
    public bool IsReplaceVisible
    {
        get => Search.IsReplaceVisible;
        set => Search.IsReplaceVisible = value;
    }

    /// <summary>Disparado ao abrir o painel por botão ou atalho; leva se é para a substituição (Ctrl+H), para a tela pôr o foco no campo certo.</summary>
    public event EventHandler<bool>? SearchOpened;

    /// <summary>O último salto para uma linha pedido pela lista de resultados; os testes o aguardam.</summary>
    public Task SearchNavigation { get; private set; } = Task.CompletedTask;

    /// <summary>Ctrl+F: abre o painel de busca. Se a substituição já estava aberta, ela continua.</summary>
    [RelayCommand]
    private void OpenSearch()
    {
        if (IsReviewing)
        {
            return;
        }

        IsSearchOpen = true;
        SearchOpened?.Invoke(this, false);
    }

    /// <summary>Ctrl+H: abre o painel com a substituição.</summary>
    [RelayCommand]
    private void OpenReplace()
    {
        if (IsReviewing)
        {
            return;
        }

        IsSearchOpen = true;
        IsReplaceVisible = true;
        SearchOpened?.Invoke(this, true);
    }

    /// <summary>O botão da barra: abre o painel, ou fecha quando ele já está aberto.</summary>
    [RelayCommand]
    private void ToggleSearch()
    {
        if (IsSearchOpen)
        {
            CloseSearch();
        }
        else
        {
            OpenSearch();
        }
    }

    /// <summary>Esc: fecha o painel. O que foi digitado fica para a próxima vez.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseSearch))]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        IsReplaceVisible = false;
    }

    /// <summary>Com o modo revisão aberto o Esc é dele: o atalho da busca não pode marcá-lo como tratado antes de a tela de revisão vê-lo.</summary>
    private bool CanCloseSearch() => !IsReviewing;

    /// <summary>Leva a grade à linha de um resultado: troca de script e tira os filtros que a escondem. Avisa quando a linha não existe mais.</summary>
    [RelayCommand]
    public async Task OpenSearchResultAsync(SearchResultRow? row)
    {
        if (row is null || row.UnitId.Length == 0)
        {
            return;
        }

        try
        {
            if (!await GoToLineAsync(row.ScriptKey, row.UnitId))
            {
                ErrorMessage = $"A linha {row.UnitId} não existe mais neste projeto. Busque de novo.";
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível abrir a linha {row.UnitId}: {e.Message}";
        }
    }

    [RelayCommand]
    private void ShowAll() => SelectFilter(chips[0]);

    [RelayCommand]
    private void SelectFilter(FilterChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        if (chip.Filter == statusFilter)
        {
            // O chip é um botão de alternar: clicar no que já vale o desmarcaria na tela.
            chip.IsSelected = true;
            return;
        }

        statusFilter = chip.Filter;
        OnPropertyChanged(nameof(HasStatusFilter));
        OnPropertyChanged(nameof(EmptyActionText));
        foreach (var other in chips)
        {
            other.IsSelected = other == chip;
        }

        Refresh();
    }

    [RelayCommand]
    public void SelectNext() => Move(+1);

    [RelayCommand]
    public void SelectPrevious() => Move(-1);

    /// <summary>Grava a edição pendente do painel de detalhe. Falso quando a gravação falha; sem painel aberto não há o que gravar.</summary>
    public bool FlushPendingEdits() => Detail?.Flush() ?? true;

    partial void OnProjectChanged(ProjectSummary? value)
    {
        StopReview();
        ClearChangedSinceTest();
        Search.Project = value;
        Loading = LoadAsync();
    }

    partial void OnSelectedLineChanged(LineViewModel? oldValue, LineViewModel? newValue)
    {
        if (revertingSelection)
        {
            return;
        }

        if (Detail is { } panel)
        {
            // Só a troca pedida pelo usuário pode ser vetada: ao trocar de script ou de projeto, ou quando o filtro tira a linha da grade, a linha antiga já não existe na lista.
            var canVeto = !discardingSelection && oldValue is not null && IndexOf(oldValue) >= 0;
            if (!panel.Flush() && canVeto)
            {
                revertingSelection = true;
                try
                {
                    SelectedLine = oldValue;
                }
                finally
                {
                    revertingSelection = false;
                }

                return;
            }

            if (oldValue is not null && !ReferenceEquals(panel.Saved, oldValue.Entry.Unit))
            {
                savedUnits[panel.Id] = panel.Saved;
            }

            panel.Dispose();
        }

        Detail = CreateDetail(newValue);
    }

    private LineDetailViewModel? CreateDetail(LineViewModel? line)
    {
        if (line is null || lineEditor is null)
        {
            return null;
        }

        var shortener = Shortener ?? suggester as ILineShortener;

        var unit = savedUnits.GetValueOrDefault(line.Id) ?? line.Entry.Unit;
        var lineContext = context.For(unit.EffectiveSpeaker, unit.Source);
        var scene = SceneAround(line);
        var sameSource = new SameSourceBinding(this, line);
        var detail = postToUi is null
            ? new LineDetailViewModel(unit, line.Entry.ScriptKey, lineEditor, this, clock) { Suggester = suggester, Shortener = shortener, Context = lineContext, ContextForSpeaker = speaker => context.For(speaker, unit.Source), Characters = context.Characters.Entries, Scene = scene, SameSource = sameSource, Consistency = sameSource, Explanations = Explanations, ProjectRoot = Project?.Root }
            : new LineDetailViewModel(unit, line.Entry.ScriptKey, lineEditor, this, clock, postToUi) { Suggester = suggester, Shortener = shortener, Context = lineContext, ContextForSpeaker = speaker => context.For(speaker, unit.Source), Characters = context.Characters.Entries, Scene = scene, SameSource = sameSource, Consistency = sameSource, Explanations = Explanations, ProjectRoot = Project?.Root };
        detail.AttachPreview(CurrentProfile(), CurrentSpeakerMarks(), CurrentSpeakerNaming());
        detail.ShowIssues(line.Entry.Issues);
        detail.SavedChanged += saved => OnUnitSaved(line, saved);
        return detail;
    }

    /// <summary>A fala anterior e a seguinte no script, mesmo que o filtro da grade as esconda.</summary>
    private List<ContextLine> SceneAround(LineViewModel line)
    {
        var scene = new List<ContextLine>(2);
        var index = line.Number - 1;
        if (index < 0 || index >= scriptLines.Length || !ReferenceEquals(scriptLines[index], line))
        {
            return scene;
        }

        foreach (var neighbour in new[] { index - 1, index + 1 })
        {
            if (neighbour >= 0 && neighbour < scriptLines.Length)
            {
                var other = scriptLines[neighbour];
                scene.Add(new ContextLine(other.SpeakerLabel, other.SourcePlain, other.HasTranslation ? other.TranslationPlain : null));
            }
        }

        return scene;
    }

    /// <summary>"Usar em todos": o que gravar a tradução nas outras linhas com o mesmo original sobrescreveria.</summary>
    private SameSourcePreview InspectSameSource(LineViewModel line, string translation)
    {
        var differing = (data?.SameSourceAs(line.Entry) ?? [])
            .Where(o => o.Unit.HasTranslation && !string.Equals(o.Unit.Translation, translation, StringComparison.Ordinal))
            .ToList();
        return new SameSourcePreview(differing.Count, differing.Count(o => o.Unit.Status is TranslationStatus.Reviewed or TranslationStatus.Approved));
    }

    /// <summary>
    /// "Usar em todos": grava a tradução nas outras linhas com o mesmo original e atualiza a grade, o QA e os contadores do que está aberto, mesmo quando um
    /// script não grava (as linhas dos scripts anteriores já estão no disco). Registra o "Desfazer" do lote.
    /// </summary>
    private SameSourceOutcome ApplyToSameSource(LineViewModel line, string translation)
    {
        if (data is null || lineEditor is null)
        {
            return new SameSourceOutcome(0, 0, 0, Failed: false);
        }

        var others = data.SameSourceAs(line.Entry).ToDictionary(o => o.Unit.Id, StringComparer.Ordinal);
        var result = lineEditor.SetTranslationFor(others.Values.Select(o => (o.ScriptKey, o.Unit.Id)), translation);
        var current = result.Changes.Select(c => c.Saved).Concat(result.Unchanged.Select(u => u.Unit));
        foreach (var unit in current)
        {
            ShowSavedUnit(others[unit.Id], unit);
        }

        foreach (var key in current.Select(u => others[u.Id].ScriptKey).Distinct(StringComparer.Ordinal))
        {
            RefreshScriptProgress(key);
        }

        if (result.Changes.Count > 0)
        {
            UpdateCounts();
            LastSavedAt = clock.GetLocalNow();
            var batch = result.Changes.Select(c => new LineSavedEventArgs(Project?.Root, c.ScriptKey, others[c.Saved.Id].Number, c.Previous, c.Saved)).ToList();
            LineSaved?.Invoke(this, batch[0] with { Batch = batch });
        }

        return new SameSourceOutcome(result.Applied, result.Requested, result.Changes.Count, result.Failure is not null);
    }

    /// <summary>
    /// O cartão de "mesmo original, traduções diferentes" da linha: os grupos do Core (<see cref="ConsistencyGroups"/>) sobre esta linha e as outras com o
    /// mesmo original, com as linhas nomeadas pela cena e pela posição, como na Conferência. Nulo quando o original tem uma tradução só ou o usuário as manteve diferentes.
    /// </summary>
    private ConferenceConsistencyCardViewModel? BuildConsistencyCard(LineViewModel line, ConsistencyCardActions actions)
    {
        if (data is null)
        {
            return null;
        }

        var all = data.SameSourceAs(line.Entry).Append(line.Entry)
            .OrderBy(e => e.ScriptKey, StringComparer.Ordinal).ThenBy(e => e.Number).ToList();
        var texts = all.GroupBy(e => e.ScriptKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)[.. g.Select(e => e.Unit)], StringComparer.Ordinal);
        if (ConsistencyGroups.Find(QaContext.ForTexts(texts)).FirstOrDefault(g => !g.IsKept) is not { } group)
        {
            return null;
        }

        var positions = all.ToDictionary(e => e.Unit.Id, e => (int?)e.Number, StringComparer.Ordinal);
        var scenes = Scripts.ToDictionary(s => s.Key, s => s.Name, StringComparer.Ordinal);
        string Label(ConsistencyLine l) => $"{scenes.GetValueOrDefault(l.ScriptKey, "Cena")} · {positions.GetValueOrDefault(l.UnitId)?.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return new ConferenceConsistencyCardViewModel(group, Label, [], positions, actions);
    }

    /// <summary>
    /// "Manter diferentes de propósito" no Editor: grava a marca do grupo (<see cref="ConsistencyFlags.Keep"/>), põe as marcas nas linhas carregadas e reconfere já,
    /// para o aviso sumir da grade e do painel sem esperar a próxima gravação. Devolve a mensagem de falha, ou nulo.
    /// </summary>
    private async Task<string?> KeepConsistencyAsync(ConsistencyGroup group)
    {
        if (data is not { } lines || Project?.Root is not { } root)
        {
            return "Não foi possível manter as traduções diferentes: o projeto não está aberto.";
        }

        try
        {
            await Task.Run(() => ConsistencyFlags.Keep(ProjectFolder.Open(root), group));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível manter as traduções diferentes: {e.Message.TrimEnd('.', ' ')}. O aviso continua.";
        }

        if (!ReferenceEquals(data, lines))
        {
            return null;
        }

        var flag = ConsistencyGroups.FlagFor(group);
        foreach (var script in group.Lines.GroupBy(l => l.ScriptKey, StringComparer.Ordinal))
        {
            var wanted = script.Select(l => l.UnitId).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in lines.LinesOf(script.Key).Where(e => wanted.Contains(e.Unit.Id)))
            {
                IReadOnlyList<string> flags = [.. entry.Unit.Flags.Where(f => !f.StartsWith(ConsistencyGroups.FlagPrefix, StringComparison.Ordinal)), flag];
                var updated = entry with { Unit = entry.Unit with { Flags = flags } };
                lines.Replace(updated);
                savedUnits.Remove(updated.Unit.Id);
                var index = entry.Number - 1;
                if (entry.ScriptKey == SelectedScript?.Key && index < scriptLines.Length)
                {
                    scriptLines[index].Update(updated);
                    entries[index] = updated;
                }
            }
        }

        await RecheckAsync();
        return null;
    }

    /// <summary>Leva uma unidade gravada por fora do painel (o "Usar em todos") para os dados, o QA e a grade do script aberto.</summary>
    private void ShowSavedUnit(LineEntry entry, TextUnit unit)
    {
        if (entry.Unit.Translation == unit.Translation && entry.Unit.Status == unit.Status)
        {
            return;
        }

        var updated = data?.WithUnit(entry, unit) ?? entry.WithUnit(unit);
        ErrorCount += ErrorsIn(updated.Issues) - ErrorsIn(entry.Issues);
        IssueCount += updated.Issues.Count - entry.Issues.Count;
        data?.Replace(updated);
        RequestRecheck();
        savedUnits.Remove(unit.Id);
        var index = entry.Number - 1;
        if (entry.ScriptKey == SelectedScript?.Key && index < scriptLines.Length)
        {
            scriptLines[index].Update(updated);
            entries[index] = updated;
        }
    }

    private sealed class SameSourceBinding(EditorViewModel editor, LineViewModel line) : ISameSourceLines, IConsistencyLines
    {
        public ConferenceConsistencyCardViewModel? Build(ConsistencyCardActions actions) => editor.BuildConsistencyCard(line, actions);

        public Task<string?> KeepAsync(ConsistencyGroup group) => editor.KeepConsistencyAsync(group);

        public Task OpenAsync(LineRequest request) => editor.GoToLineAsync(request.ScriptKey, request.UnitId);

        public int Count => editor.data?.SameSourceAs(line.Entry).Count ?? 0;

        public SameSourcePreview Inspect(string translation) => editor.InspectSameSource(line, translation);

        public SameSourceOutcome Apply(string translation) => editor.ApplyToSameSource(line, translation);
    }

    /// <summary>
    /// O painel gravou a linha: a grade, os contadores dos filtros e o QA da linha passam a refletir a unidade nova. A linha continua onde está
    /// mesmo que o filtro ativo deixe de aceitá-la; o filtro só é reavaliado na próxima troca.
    /// </summary>
    private void OnUnitSaved(LineViewModel line, TextUnit saved)
    {
        var previous = line.Entry.Unit;
        var updated = data?.WithUnit(line.Entry, saved) ?? line.Entry.WithUnit(saved);
        ErrorCount += ErrorsIn(updated.Issues) - ErrorsIn(line.Entry.Issues);
        IssueCount += updated.Issues.Count - line.Entry.Issues.Count;
        LastSavedAt = clock.GetLocalNow();
        line.Update(updated, context.Characters.Find(saved.EffectiveSpeaker)?.Target);
        if (Detail is { } open && string.Equals(open.Id, line.Id, StringComparison.Ordinal))
        {
            open.ShowIssues(updated.Issues);
        }

        // Voltar a este script reconstrói as linhas a partir de data: a edição tem de estar lá.
        data?.Replace(updated);
        RequestRecheck();
        RefreshScriptProgress(updated.ScriptKey);

        // O painel descartado ao trocar de script grava por último; a linha dele já não é a do script atual.
        var index = line.Number - 1;
        if (index < scriptLines.Length && ReferenceEquals(scriptLines[index], line))
        {
            entries[index] = updated;
            UpdateCounts();
        }

        RecordApproval(line.Entry.ScriptKey, previous, saved);
        if (previous.Translation != saved.Translation || previous.Status != saved.Status)
        {
            LineSaved?.Invoke(this, new LineSavedEventArgs(Project?.Root, line.Entry.ScriptKey, line.Number, previous, saved));
        }
    }

    /// <summary>Desfaz uma gravação: devolve a linha ao que ela era no arquivo e relê o Editor. Falso (com <see cref="ErrorMessage"/>) quando não deu para gravar.</summary>
    public bool Undo(LineSavedEventArgs saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (lineEditor is null || Project?.Root != saved.ProjectRoot)
        {
            ErrorMessage = $"Não foi possível desfazer {(saved.IsBatch ? "a gravação das linhas" : $"a gravação da linha {saved.Number}")}: o projeto aberto mudou.";
            return false;
        }

        if (!FlushPendingEdits())
        {
            return false;
        }

        try
        {
            foreach (var item in saved.IsBatch ? saved.Batch : [saved])
            {
                lineEditor.Restore(item.ScriptKey, item.Previous, item.Saved);
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            ErrorMessage = $"Não foi possível desfazer {(saved.IsBatch ? "a gravação das linhas" : $"a gravação da linha {saved.Number}")}: {e.Message}";
            return false;
        }

        ForgetApprovals(saved);
        Reload();
        return true;
    }

    /// <summary>
    /// Refaz a prévia da linha aberta com o <c>formatting.json</c> de agora. A página do Editor chama ao voltar à tela, depois de Ajustes ter gravado o perfil;
    /// sem mudança no arquivo, a prévia fica como está.
    /// </summary>
    public void RefreshPreview() => Detail?.AttachPreview(CurrentProfile(), CurrentSpeakerMarks(), CurrentSpeakerNaming());

    /// <summary>O perfil do projeto, relido só quando o arquivo muda (<see cref="FormattingProfileCache"/>). Sem perfil legível, o painel fica sem prévia.</summary>
    private FormattingProfile? CurrentProfile() => Project is { } project ? profiles.Get(project.Root) : null;

    /// <summary>A abertura e o fechamento que o jogo desenha em volta da fala com falante, para a prévia medir a caixa como ela aparece no jogo (#453).</summary>
    private SpeakerBoxMarks CurrentSpeakerMarks() => Project is { } project ? speakerMarks.Get(project.Root) : SpeakerBoxMarks.None;

    /// <summary>De onde a placa da caixa do jogo tira o nome: os personagens do Glossário e a codificação do projeto, como o build (#464). A mesma instância enquanto nenhum dos dois muda.</summary>
    private SpeakerNaming CurrentSpeakerNaming()
    {
        if (Project is not { } project)
        {
            return SpeakerNaming.None;
        }

        var encoder = speakerMarks.GetEncoder(project.Root);
        if (speakerNaming is { } current && ReferenceEquals(current.Characters, context.Characters) && ReferenceEquals(current.Encoder, encoder))
        {
            return current;
        }

        return speakerNaming = new SpeakerNaming(context.Characters, encoder);
    }

    private LineEditor? CreateLineEditor(ProjectLines? loaded, string root)
    {
        if (loaded is null)
        {
            return null;
        }

        return EditorFactory is { } factory ? factory(root) : TryCreateEditor(root);
    }

    private LineEditor? TryCreateEditor(string root)
    {
        try
        {
            return new LineEditor(ProjectFolder.Open(root), clock);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    partial void OnSelectedScriptChanged(ScriptItemViewModel? value)
    {
        if (!applying)
        {
            BuildScript();
        }
    }

    partial void OnSelectedSpeakerChanged(SpeakerOption? value)
    {
        if (!applying)
        {
            UpdateCounts();
            Refresh();
        }
    }

    private void Apply(ProjectLines? loaded)
    {
        // As linhas da fila do modo revisão são as de antes da releitura (um "Desfazer", uma busca com substituição): a fila já não vale.
        StopReview();
        applying = true;
        try
        {
            data = loaded;
            ErrorCount = loaded is null ? 0 : CountErrors(loaded);
            IssueCount = loaded is null ? 0 : CountIssues(loaded);
            Scripts.Clear();
            foreach (var script in loaded?.Scripts ?? [])
            {
                Scripts.Add(new ScriptItemViewModel(script, SceneLabel(script.Key)) { IsRunning = script.Key == RunningScriptKey, Number = Scripts.Count + 1, Position = Scripts.Count + 1 });
            }

            RebuildChapters();
            ApplyScriptFilter();

            // A cena vazia vem escondida da lista: abrir o projeto numa delas deixaria o Editor sem linhas e sem item marcado.
            SelectedScript = Scripts.FirstOrDefault(s => !s.IsEmpty) ?? Scripts.FirstOrDefault();
        }
        finally
        {
            applying = false;
        }

        OnPropertyChanged(nameof(ScriptsCount));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(VisibleScripts));
        BuildScript();
    }

    /// <summary>Cria os <see cref="LineViewModel"/> do script atual, uma vez; os filtros só escolhem entre eles.</summary>
    private void BuildScript()
    {
        entries = SelectedScript is { } script && data is not null ? [.. data.LinesOf(script.Key)] : [];
        scriptLines = new LineViewModel[entries.Length];
        for (var i = 0; i < scriptLines.Length; i++)
        {
            scriptLines[i] = new LineViewModel(entries[i], context.Characters.Find(entries[i].Unit.EffectiveSpeaker)?.Target);
        }

        applying = true;
        try
        {
            Speakers = SpeakerOptionsOf(entries);
            SelectedSpeaker = Speakers[0];
        }
        finally
        {
            applying = false;
        }

        discardingSelection = true;
        try
        {
            SelectedLine = null;
        }
        finally
        {
            discardingSelection = false;
        }

        UpdateCounts();
        Refresh();
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(ScriptTitle));
        OnPropertyChanged(nameof(Distribution));
        OnPropertyChanged(nameof(DistributionDescription));
    }

    /// <summary>O nome da cena: o que o chamador dá ou, sem ele, o do <c>scripts.json</c> (o do usuário, senão o que a engine leu do jogo).</summary>
    private string? SceneLabel(string key) => scriptLabel(key) ?? scriptNames.DisplayName(key);

    private static List<SpeakerOption> SpeakerOptionsOf(IReadOnlyList<LineEntry> lines)
    {
        var options = new List<SpeakerOption> { new(AllSpeakersLabel, null) };
        if (lines.Any(l => string.IsNullOrEmpty(l.Unit.EffectiveSpeaker)))
        {
            options.Add(new SpeakerOption("Narração", LineFilter.Narration));
        }

        options.AddRange(ProjectLines.SpeakersOf(lines).Select(name => new SpeakerOption(name, name)));
        return options;
    }

    /// <summary>
    /// Recalcula o percentual e a barra de distribuição do script <paramref name="key"/> na lista de Cenas a partir das linhas já carregadas
    /// (<see cref="data"/>), para que uma gravação ou o "Usar em todos" apareçam sem reler o projeto. O rodapé acompanha quando o script é o aberto.
    /// </summary>
    private void RefreshScriptProgress(string key)
    {
        var item = Scripts.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));
        if (item is null || data is null)
        {
            return;
        }

        item.Update(StatusCounts.Of(data.LinesOf(key).Select(l => l.Unit)));
        if (ReferenceEquals(item, SelectedScript))
        {
            OnPropertyChanged(nameof(Distribution));
            OnPropertyChanged(nameof(DistributionDescription));
        }
    }

    private void UpdateCounts()
    {
        var speaker = SelectedSpeaker?.Value;
        foreach (var chip in chips)
        {
            chip.Count = FilterFor(chip.Filter, speaker).Count(entries);
        }

        PendingLines = new LineFilter(LineStatusFilter.Pending).Count(entries);
        OnPropertyChanged(nameof(PendingLines));
        TranslatePendingCommand.NotifyCanExecuteChanged();
    }

    private void Refresh()
    {
        var filter = FilterFor(statusFilter, SelectedSpeaker?.Value);
        var filtered = filter.Apply(entries);

        IReadOnlyList<LineViewModel> next = scriptLines;
        if (!ReferenceEquals(filtered, entries))
        {
            var picked = new LineViewModel[filtered.Count];
            for (var i = 0; i < picked.Length; i++)
            {
                picked[i] = scriptLines[filtered[i].Number - 1];
            }

            next = picked;
        }

        var keep = SelectedLine is { } current && filter.Matches(current.Entry) ? current : null;
        Lines = next;
        SelectedLine = keep ?? (next.Count > 0 ? next[0] : null);
    }

    private void Move(int delta)
    {
        if (Lines.Count == 0)
        {
            return;
        }

        var index = SelectedLine is null ? -1 : IndexOf(SelectedLine);
        SelectedLine = index < 0 ? Lines[0] : Lines[Math.Clamp(index + delta, 0, Lines.Count - 1)];
    }

    /// <summary>Posição de <paramref name="line"/> em <see cref="Lines"/> (ordenada pelo número da linha); -1 quando não está.</summary>
    private int IndexOf(LineViewModel line)
    {
        int low = 0, high = Lines.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var number = Lines[middle].Number;
            if (number == line.Number)
            {
                return ReferenceEquals(Lines[middle], line) ? middle : -1;
            }

            if (number < line.Number)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }
}
