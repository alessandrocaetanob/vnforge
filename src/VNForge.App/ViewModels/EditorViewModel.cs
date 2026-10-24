using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Uma opção do seletor de personagem. <see cref="Value"/> é o <see cref="LineFilter.Speaker"/>: nulo para todos, <see cref="LineFilter.Narration"/> para a narração.</summary>
public sealed record SpeakerOption(string Label, string? Value);

/// <summary>
/// Página do Editor: lista de scripts com progresso, grade de linhas filtrável e a coluna do detalhe.
/// A grade recebe <see cref="Lines"/> trocada de uma vez a cada filtro; mexer numa coleção observável de dezenas de milhares de linhas travaria a tela.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject, ILineNavigator
{
    private const string AllSpeakersLabel = "Todos os personagens";

    private readonly Func<string, ProjectLines> loader;
    private readonly FilterChipViewModel[] chips;
    private readonly TimeProvider clock;
    private readonly Action<Action>? postToUi;
    private readonly Dictionary<string, TextUnit> savedUnits = new(StringComparer.Ordinal);
    private LineEditor? lineEditor;
    private ProjectLines? data;
    private IReadOnlyList<LineEntry> entries = [];
    private LineViewModel[] scriptLines = [];
    private LineStatusFilter statusFilter = LineStatusFilter.All;
    private int loadVersion;
    private bool applying;
    private bool revertingSelection;
    private bool discardingSelection;

    /// <param name="loader">Lê as linhas do projeto; o padrão lê <c>text/*.json</c>.</param>
    /// <param name="clock">Relógio do painel de detalhe (debounce da gravação e horário da origem <c>manual</c>); o padrão é o do sistema.</param>
    /// <param name="postToUi">Como o painel executa a gravação agendada na thread da tela; nulo usa o <see cref="SynchronizationContext"/> da thread que o criou. Os testes passam uma chamada direta.</param>
    public EditorViewModel(Func<string, ProjectLines>? loader = null, TimeProvider? clock = null, Action<Action>? postToUi = null)
    {
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial bool IsLoading { get; set; }

    /// <summary>Falha ao ler as linhas: o fato e a consequência, em português.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial string? ErrorMessage { get; set; }

    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = [];

    [ObservableProperty]
    public partial ScriptItemViewModel? SelectedScript { get; set; }

    public IReadOnlyList<FilterChipViewModel> Filters => chips;

    [ObservableProperty]
    public partial IReadOnlyList<SpeakerOption> Speakers { get; private set; } = [new SpeakerOption(AllSpeakersLabel, null)];

    [ObservableProperty]
    public partial SpeakerOption? SelectedSpeaker { get; set; }

    /// <summary>As linhas visíveis. É trocada por inteiro a cada filtro ou script; nunca recebe <c>Add</c> nem <c>Remove</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
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

    public string ProjectName => Project?.Name ?? string.Empty;

    public string ScriptTitle => SelectedScript?.Key ?? string.Empty;

    public string ScriptsCount => ProjectLabels.Number(Scripts.Count);

    public StatusDistribution? Distribution => SelectedScript?.Distribution;

    public string DistributionDescription => SelectedScript?.DistributionDescription ?? string.Empty;

    /// <summary>Total do script; com filtro ativo, "12 de 890 linhas neste script".</summary>
    public string LinesSummary
    {
        get
        {
            var total = entries.Count;
            var noun = total == 1 ? "linha" : "linhas";
            return Lines.Count == total
                ? $"{ProjectLabels.Number(total)} {noun} neste script"
                : $"{ProjectLabels.Number(Lines.Count)} de {ProjectLabels.Number(total)} {noun} neste script";
        }
    }

    public bool IsEmpty => Lines.Count == 0;

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

        if (root is null)
        {
            IsLoading = false;
            Apply(null);
            return;
        }

        // As linhas do projeto anterior não podem ficar na tela como se fossem do novo enquanto ele carrega.
        Apply(null);
        lineEditor = null;
        savedUnits.Clear();
        IsLoading = true;
        ProjectLines? loaded = null;
        string? error = null;
        try
        {
            loaded = await Task.Run(() => loader(root));
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
        Apply(loaded);
    }

    [RelayCommand]
    private void SelectFilter(FilterChipViewModel? chip)
    {
        if (chip is null || chip.Filter == statusFilter)
        {
            return;
        }

        statusFilter = chip.Filter;
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

    partial void OnProjectChanged(ProjectSummary? value) => Loading = LoadAsync();

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

        var unit = savedUnits.GetValueOrDefault(line.Id) ?? line.Entry.Unit;
        return postToUi is null
            ? new LineDetailViewModel(unit, line.Entry.ScriptKey, lineEditor, this, clock)
            : new LineDetailViewModel(unit, line.Entry.ScriptKey, lineEditor, this, clock, postToUi);
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
        applying = true;
        try
        {
            data = loaded;
            Scripts.Clear();
            foreach (var script in loaded?.Scripts ?? [])
            {
                Scripts.Add(new ScriptItemViewModel(script));
            }

            SelectedScript = Scripts.FirstOrDefault();
        }
        finally
        {
            applying = false;
        }

        OnPropertyChanged(nameof(ScriptsCount));
        OnPropertyChanged(nameof(EmptyMessage));
        BuildScript();
    }

    /// <summary>Cria os <see cref="LineViewModel"/> do script atual, uma vez; os filtros só escolhem entre eles.</summary>
    private void BuildScript()
    {
        entries = SelectedScript is { } script && data is not null ? data.LinesOf(script.Key) : [];
        scriptLines = new LineViewModel[entries.Count];
        for (var i = 0; i < scriptLines.Length; i++)
        {
            scriptLines[i] = new LineViewModel(entries[i]);
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
