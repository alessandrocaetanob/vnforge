using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Uma gravação de linha que mudou a tradução ou o status: a unidade de antes e a de depois, para o aviso "Desfazer".</summary>
public sealed record LineSavedEventArgs(string? ProjectRoot, string ScriptKey, int Number, TextUnit Previous, TextUnit Saved)
{
    public bool IsApproval => Saved.Status == TranslationStatus.Approved && Previous.Status != TranslationStatus.Approved;
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
    /// <param name="scriptLabel">Dá o nome legível de um script pela chave dele; nulo mostra só a chave.</param>
    /// <param name="search">O painel de busca e substituição; o padrão cria o <see cref="SearchReplaceViewModel"/> do projeto.</param>
    public EditorViewModel(
        Func<string, ProjectLines>? loader = null,
        TimeProvider? clock = null,
        Action<Action>? postToUi = null,
        ILineSuggester? suggester = null,
        Func<string, EditorContext>? contextLoader = null,
        Func<string, string?>? scriptLabel = null,
        SearchReplaceViewModel? search = null)
    {
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
            new FilterChipViewModel(LineStatusFilter.Pending, "Pendentes"),
            new FilterChipViewModel(LineStatusFilter.Machine, "IA"),
            new FilterChipViewModel(LineStatusFilter.Warnings, "Com aviso"),
        ];
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

    public IReadOnlyList<FilterChipViewModel> Filters => chips;

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

    public string ProjectName => Project?.Name ?? string.Empty;

    /// <summary>A linha de cima do cabeçalho, em mono: a chave do script aberto.</summary>
    public string Breadcrumb => SelectedScript?.Key ?? string.Empty;

    /// <summary>O título do cabeçalho: o nome do capítulo, ou a chave quando o projeto ainda não guarda o nome.</summary>
    public string ScriptTitle => SelectedScript?.Display ?? string.Empty;

    /// <summary>Linhas do script aberto que ainda não têm tradução.</summary>
    public int PendingLines { get; private set; }

    public string ScriptsCount => ProjectLabels.Number(Scripts.Count);

    public StatusDistribution? Distribution => SelectedScript?.Distribution;

    public string DistributionDescription => SelectedScript?.DistributionDescription ?? string.Empty;

    /// <summary>Total do script; com filtro ativo, "12 de 890 linhas neste script".</summary>
    public string LinesSummary
    {
        get
        {
            var total = entries.Length;
            var noun = total == 1 ? "linha" : "linhas";
            return Lines.Count == total
                ? $"{ProjectLabels.Number(total)} {noun} neste script"
                : $"{ProjectLabels.Number(Lines.Count)} de {ProjectLabels.Number(total)} {noun} neste script";
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
        string? error = null;
        try
        {
            (loaded, loadedContext) = await Task.Run(() => (loader(root), contextLoader(root)));
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
        lineEditor = loaded is null ? null : TryCreateEditor(root);
        context = loadedContext;
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

        // Só a tradução em português perde o acento na busca; no original em japonês a dobra juntaria kana com e sem dakuten.
        var folded = CommandPaletteViewModel.Fold(text);
        foreach (var script in data.Scripts)
        {
            foreach (var entry in data.LinesOf(script.Key))
            {
                var unit = entry.Unit;
                if (unit.Source.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (unit.Translation is { } translation && CommandPaletteViewModel.Fold(translation).Contains(folded, StringComparison.Ordinal)))
                {
                    found.Add(entry);
                    if (found.Count >= limit)
                    {
                        return found;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Deixa só as linhas pendentes na grade, como o chip "Pendentes".</summary>
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
    }

    /// <summary>Disparado por "Conferir arquivo": a janela abre a Conferência. Leva a chave do script aberto.</summary>
    public event EventHandler<string>? CheckRequested;

    /// <summary>Disparado por "Traduzir com IA": a janela com as três perguntas abre. Leva a chave do script aberto.</summary>
    public event EventHandler<string>? TranslateRequested;

    [RelayCommand(CanExecute = nameof(HasScript))]
    private void CheckFile() => CheckRequested?.Invoke(this, SelectedScript!.Key);

    [RelayCommand(CanExecute = nameof(CanTranslatePending))]
    private void TranslatePending() => TranslateRequested?.Invoke(this, SelectedScript!.Key);

    private bool HasScript() => SelectedScript is not null;

    // A janela oferece também o capítulo e o jogo todo, então abre mesmo quando a cena aberta não tem pendentes.
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
        IsSearchOpen = true;
        SearchOpened?.Invoke(this, false);
    }

    /// <summary>Ctrl+H: abre o painel com a substituição.</summary>
    [RelayCommand]
    private void OpenReplace()
    {
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
    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        IsReplaceVisible = false;
    }

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
        var lineContext = context.For(unit.Speaker, unit.Source);
        var detail = postToUi is null
            ? new LineDetailViewModel(unit, line.Entry.ScriptKey, lineEditor, this, clock) { Suggester = suggester, Shortener = shortener, Context = lineContext }
            : new LineDetailViewModel(unit, line.Entry.ScriptKey, lineEditor, this, clock, postToUi) { Suggester = suggester, Shortener = shortener, Context = lineContext };
        detail.AttachPreview(CurrentProfile());
        detail.SavedChanged += saved => OnUnitSaved(line, saved);
        return detail;
    }

    /// <summary>
    /// O painel gravou a linha: a grade, os contadores dos filtros e o QA da linha passam a refletir a unidade nova. A linha continua onde está
    /// mesmo que o filtro ativo deixe de aceitá-la; o filtro só é reavaliado na próxima troca.
    /// </summary>
    private void OnUnitSaved(LineViewModel line, TextUnit saved)
    {
        var previous = line.Entry.Unit;
        var updated = line.Entry.WithUnit(saved);
        IssueCount += updated.Issues.Count - line.Entry.Issues.Count;
        LastSavedAt = clock.GetLocalNow();
        line.Update(updated);

        // Voltar a este script reconstrói as linhas a partir de data: a edição tem de estar lá.
        data?.Replace(updated);

        // O painel descartado ao trocar de script grava por último; a linha dele já não é a do script atual.
        var index = line.Number - 1;
        if (index < scriptLines.Length && ReferenceEquals(scriptLines[index], line))
        {
            entries[index] = updated;
            UpdateCounts();
        }

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
            ErrorMessage = $"Não foi possível desfazer a gravação da linha {saved.Number}: o projeto aberto mudou.";
            return false;
        }

        if (!FlushPendingEdits())
        {
            return false;
        }

        try
        {
            lineEditor.Restore(saved.ScriptKey, saved.Previous, saved.Saved);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            ErrorMessage = $"Não foi possível desfazer a gravação da linha {saved.Number}: {e.Message}";
            return false;
        }

        Reload();
        return true;
    }

    /// <summary>
    /// Refaz a prévia da linha aberta com o <c>formatting.json</c> de agora. A página do Editor chama ao voltar à tela, depois de Ajustes ter gravado o perfil;
    /// sem mudança no arquivo, a prévia fica como está.
    /// </summary>
    public void RefreshPreview() => Detail?.AttachPreview(CurrentProfile());

    /// <summary>O perfil do projeto, relido só quando o arquivo muda (<see cref="FormattingProfileCache"/>). Sem perfil legível, o painel fica sem prévia.</summary>
    private FormattingProfile? CurrentProfile() => Project is { } project ? profiles.Get(project.Root) : null;

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
        applying = true;
        try
        {
            data = loaded;
            IssueCount = loaded is null ? 0 : loaded.Scripts.Sum(script => loaded.LinesOf(script.Key).Sum(line => line.Issues.Count));
            Scripts.Clear();
            foreach (var script in loaded?.Scripts ?? [])
            {
                Scripts.Add(new ScriptItemViewModel(script, scriptLabel(script.Key)) { IsRunning = script.Key == RunningScriptKey });
            }

            ApplyScriptFilter();

            SelectedScript = Scripts.FirstOrDefault();
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
            scriptLines[i] = new LineViewModel(entries[i], context.Characters.Find(entries[i].Unit.Speaker)?.Target);
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

    private static List<SpeakerOption> SpeakerOptionsOf(IReadOnlyList<LineEntry> lines)
    {
        var options = new List<SpeakerOption> { new(AllSpeakersLabel, null) };
        if (lines.Any(l => string.IsNullOrEmpty(l.Unit.Speaker)))
        {
            options.Add(new SpeakerOption("Narração", LineFilter.Narration));
        }

        options.AddRange(ProjectLines.SpeakersOf(lines).Select(name => new SpeakerOption(name, name)));
        return options;
    }

    private void UpdateCounts()
    {
        var speaker = SelectedSpeaker?.Value;
        foreach (var chip in chips)
        {
            chip.Count = new LineFilter(chip.Filter, speaker).Count(entries);
        }

        PendingLines = new LineFilter(LineStatusFilter.Pending).Count(entries);
        OnPropertyChanged(nameof(PendingLines));
        TranslatePendingCommand.NotifyCanExecuteChanged();
    }

    private void Refresh()
    {
        var filter = new LineFilter(statusFilter, SelectedSpeaker?.Value);
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
