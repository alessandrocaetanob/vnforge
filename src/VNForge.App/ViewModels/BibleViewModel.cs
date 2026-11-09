using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core;
using VNForge.Core.Bible;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Os filtros da lista de cenas da Bíblia.</summary>
public enum BibleFilter
{
    All,
    Edited,
    NoSummary,
}

internal static class BibleText
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Plural(int count, string one, string many) => count == 1 ? one : many;
}

/// <summary>Uma cena na lista da Bíblia: o número, o nome (o da bíblia, senão o do jogo) e se ainda não tem resumo.</summary>
public sealed record BibleSceneRow(string Key, int Number, string Name, bool HasSummary, bool IsEdited)
{
    public string Title => string.Create(BibleText.PtBr, $"Cena {Number}");

    public string Subtitle => HasSummary || Name.Length > 0 ? Name : "Sem resumo ainda";

    public string Mark => IsEdited ? "Sua" : EmptyMark;

    private string EmptyMark => HasSummary ? string.Empty : "vazio";

    public bool ShowCheck => HasSummary && !IsEdited;
}

/// <summary>Onde a cena aberta está na linha do tempo: os marcos de antes já passaram, o do capítulo dela é o atual.</summary>
public enum BibleTimelineState
{
    Upcoming,
    Current,
    Passed,
}

/// <summary>Um marco da linha do tempo do jogo, como a tela o mostra.</summary>
public sealed record BibleTimelineRow(string Title, string Note, BibleTimelineState State = BibleTimelineState.Upcoming)
{
    public bool IsCurrent => State == BibleTimelineState.Current;

    public bool IsPassed => State == BibleTimelineState.Passed;

    public bool IsUpcoming => State == BibleTimelineState.Upcoming;
}

/// <summary>Onde uma cena fica no jogo: o capítulo e as linhas dela, contadas desde a primeira do jogo.</summary>
internal sealed record SceneLocation(string Chapter, int? ChapterNumber, int FirstLine, int LastLine);

/// <summary>
/// A página Bíblia (#530): o que a IA entendeu de cada cena, para a pessoa ler e corrigir. Cada campo é da IA ou da pessoa; escrever num campo o torna "Sua"
/// e a geração seguinte não o troca. "Gerar de novo" mostra o preço antes de rodar. A leitura e a gravação são as do <see cref="BibleRunner"/> e do
/// <c>bible.json</c>, as mesmas do <c>vnf bible</c>.
/// </summary>
public sealed partial class BibleViewModel : ObservableObject
{
    private readonly List<BibleSceneRow> allRows = [];
    private ProjectSummary? project;
    private GameBible bible = GameBible.Empty;
    private bool loadingScene;
    private IReadOnlyList<BibleChapterInput> planned = [];
    private decimal plannedBrl;
    private Dictionary<string, SceneLocation> locations = new(StringComparer.Ordinal);
    private CancellationTokenSource? run;

    /// <summary>Monta o provedor da IA para (modelo, esforço); nulo quando falta a chave. A cena de exemplo usa o provedor de demonstração.</summary>
    public Func<string, ClaudeEffort, IBibleProvider>? ProviderFactory { get; set; }

    public Func<ExchangeRate> Rate { get; set; } = () => ExchangeRate.Placeholder;

    public Func<BudgetLimits> Limits { get; set; } = () => BudgetLimits.None;

    public ObservableCollection<BibleSceneRow> Rows { get; } = [];

    public ObservableCollection<BibleTimelineRow> Timeline { get; } = [];

    public bool HasProject => project is not null;

    [ObservableProperty]
    public partial BibleFilter Filter { get; set; } = BibleFilter.All;

    [ObservableProperty]
    public partial BibleSceneRow? SelectedRow { get; set; }

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; private set; } = string.Empty;

    // Contagens dos chips de filtro.
    public int AllCount => allRows.Count;

    public int EditedCount => allRows.Count(r => r.IsEdited);

    public int NoSummaryCount => allRows.Count(r => !r.HasSummary);

    public int NamedCount => bible.Scenes.Count(s => !string.IsNullOrWhiteSpace(s.Name));

    // A cena selecionada.
    public bool HasScene => SelectedRow is not null;

    /// <summary>"Capítulo 1 · Manhã de chuva · cena 13 · linhas 0478 a 0512"; até o jogo ser lido, só a cena.</summary>
    public string SceneHeader
    {
        get
        {
            if (SelectedRow is not { } row)
            {
                return string.Empty;
            }

            var scene = $"cena {row.Number}";
            if (!locations.TryGetValue(row.Key, out var place))
            {
                return $"{row.Title} · {row.Key}";
            }

            var chapter = place.ChapterNumber is { } n ? $"Capítulo {n} · {place.Chapter}" : place.Chapter;
            return $"{chapter} · {scene} · linhas {place.FirstLine:D4} a {place.LastLine:D4}";
        }
    }

    /// <summary>O que se digita em "Filtrar cenas": vale para o título, o nome e a chave da cena.</summary>
    [ObservableProperty]
    public partial string SceneSearch { get; set; } = string.Empty;

    /// <summary>O número de cenas da lista; com filtro, "3 de 312".</summary>
    public string ScenesCountText => Rows.Count == allRows.Count
        ? allRows.Count.ToString(BibleText.PtBr)
        : string.Create(BibleText.PtBr, $"{Rows.Count} de {allRows.Count}");

    /// <summary>A página pede ao Editor que abra o script da cena selecionada.</summary>
    public event EventHandler<string>? EditorRequested;

    [RelayCommand(CanExecute = nameof(HasScene))]
    private void OpenInEditor()
    {
        if (SelectedRow is { } row)
        {
            EditorRequested?.Invoke(this, row.Key);
        }
    }

    [ObservableProperty]
    public partial string NameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CharactersText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ToneText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string JokesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RelationsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OwnerOfName { get; private set; } = "IA";

    [ObservableProperty]
    public partial string OwnerOfSummary { get; private set; } = "IA";

    [ObservableProperty]
    public partial string OwnerOfCharacters { get; private set; } = "IA";

    [ObservableProperty]
    public partial string OwnerOfTone { get; private set; } = "IA";

    [ObservableProperty]
    public partial string OwnerOfJokes { get; private set; } = "IA";

    [ObservableProperty]
    public partial string OwnerOfRelations { get; private set; } = "IA";

    // O jogo.
    [ObservableProperty]
    public partial string SynopsisText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OwnerOfSynopsis { get; private set; } = "IA";

    public string GeneratedAtText => bible.Generation is { } g ? g.At.ToLocalTime().ToString("dd/MM, HH:mm", BibleText.PtBr) : "ainda não";

    public string ScenesReadText => bible.Generation is { } g
        ? $"{g.ScenesRead.ToString("N0", BibleText.PtBr)} em {g.Chapters} {BibleText.Plural(g.Chapters, "capítulo", "capítulos")}"
        : "nenhuma";

    public string CostText => bible.Generation is { } g ? VNForge.App.Controls.Money.Brl(Rate().ToBrl(g.Dollars)) : "sem custo ainda";

    public string RereadText { get; private set; } = "calculado ao abrir a geração";

    public bool CanUndo => project is not null && ProjectFolder.Open(project.Root).HasPreviousBible;

    // A geração.
    [ObservableProperty]
    public partial bool IsGenerateOpen { get; set; }

    [ObservableProperty]
    public partial bool IsEstimating { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial string ReadText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EstimateText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SpentText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EditedKeptText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double SpentFraction { get; private set; }

    [ObservableProperty]
    public partial double EstimateFraction { get; private set; }

    public string GenerateLabel => $"Gerar de novo · {CostButtonText}";

    [ObservableProperty]
    public partial string CostButtonText { get; private set; } = "ver preço";

    /// <summary>Termina quando a última carga da página acabou.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Termina quando a última geração pedida acabou.</summary>
    public Task Generation { get; private set; } = Task.CompletedTask;

    /// <summary>Abre a página sobre um projeto e lê a bíblia.</summary>
    public void Open(ProjectSummary summary)
    {
        project = summary;
        OnPropertyChanged(nameof(HasProject));
        Reload();
    }

    /// <summary>Relê o <c>bible.json</c> e a lista de cenas, mantendo a cena selecionada.</summary>
    public void Reload()
    {
        if (project is not { } current)
        {
            return;
        }

        var folder = ProjectFolder.Open(current.Root);
        ErrorMessage = string.Empty;
        bible = folder.LoadBibleOrEmpty(out var problem);
        if (problem is not null)
        {
            ErrorMessage = problem;
        }

        var names = SafeNames(folder);
        var keys = ScriptGrouping.InStoryOrder(Directory.Exists(folder.TextDirectory)
            ? Directory.EnumerateFiles(folder.TextDirectory, "*.json").Select(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant())
            : []);
        allRows.Clear();
        for (var i = 0; i < keys.Count; i++)
        {
            allRows.Add(RowOf(keys[i], i + 1, names));
        }

        var selectedKey = SelectedRow?.Key;
        ApplyFilter();
        SelectedRow = Rows.FirstOrDefault(r => r.Key == selectedKey) ?? Rows.FirstOrDefault();
        LoadScene();
        LoadGame();
        OnPropertyChanged(string.Empty);
        Loading = EstimateAsync();
    }

    private BibleSceneRow RowOf(string key, int number, ScriptNames names)
    {
        var scene = bible.Scene(key);
        var name = scene is { } s && !string.IsNullOrWhiteSpace(s.Name) ? s.Name : names.DisplayName(key) ?? string.Empty;
        return new BibleSceneRow(key, number, name, scene is { Summary.Length: > 0 }, scene is { Edited.Count: > 0 });
    }

    private static ScriptNames SafeNames(ProjectFolder folder)
    {
        try
        {
            return folder.LoadScriptNames();
        }
        catch (VNForgeException)
        {
            return ScriptNames.Empty;
        }
    }

    partial void OnFilterChanged(BibleFilter value)
    {
        var key = SelectedRow?.Key;
        ApplyFilter();
        SelectedRow = Rows.FirstOrDefault(r => r.Key == key) ?? Rows.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var search = SceneSearch.Trim();
        foreach (var row in allRows.Where(r => Filter switch
        {
            BibleFilter.Edited => r.IsEdited,
            BibleFilter.NoSummary => !r.HasSummary,
            _ => true,
        }).Where(r => search.Length == 0
            || r.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || r.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || r.Key.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(ScenesCountText));
    }

    partial void OnSceneSearchChanged(string value)
    {
        var key = SelectedRow?.Key;
        ApplyFilter();
        SelectedRow = Rows.FirstOrDefault(r => r.Key == key) ?? Rows.FirstOrDefault();
    }

    partial void OnSelectedRowChanged(BibleSceneRow? value)
    {
        LoadScene();
        OnPropertyChanged(nameof(HasScene));
        OnPropertyChanged(nameof(SceneHeader));
        OpenInEditorCommand.NotifyCanExecuteChanged();
        RefreshTimeline();
    }

    private void LoadScene()
    {
        loadingScene = true;
        var scene = SelectedRow is { } row ? bible.Scene(row.Key) : null;
        NameText = scene?.Name ?? string.Empty;
        SummaryText = scene?.Summary ?? string.Empty;
        CharactersText = string.Join("; ", scene?.Characters ?? []);
        ToneText = string.Join("; ", scene?.Tone ?? []);
        JokesText = string.Join("; ", scene?.Jokes ?? []);
        RelationsText = string.Join('\n', (scene?.Relations ?? []).Select(r => $"{r.Between}: {r.Note}"));
        OwnerOfName = Owner(scene, BibleField.Name);
        OwnerOfSummary = Owner(scene, BibleField.Summary);
        OwnerOfCharacters = Owner(scene, BibleField.Characters);
        OwnerOfTone = Owner(scene, BibleField.Tone);
        OwnerOfJokes = Owner(scene, BibleField.Jokes);
        OwnerOfRelations = Owner(scene, BibleField.Relations);
        loadingScene = false;
    }

    private void LoadGame()
    {
        loadingScene = true;
        SynopsisText = bible.Synopsis;
        OwnerOfSynopsis = bible.SynopsisEdited ? "Sua" : "IA";
        loadingScene = false;
        RefreshTimeline();
    }

    /// <summary>Remonta a linha do tempo marcando o capítulo da cena aberta como o atual e os de antes como passados.</summary>
    private void RefreshTimeline()
    {
        var entries = bible.Timeline;
        var current = SelectedRow is { } row && locations.TryGetValue(row.Key, out var place)
            ? entries.ToList().FindIndex(t => string.Equals(t.Title, place.Chapter, StringComparison.Ordinal))
            : -1;
        Timeline.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            Timeline.Add(new BibleTimelineRow(entries[i].Title, entries[i].Note, StateOf(i, current)));
        }
    }

    private static BibleTimelineState StateOf(int index, int current)
    {
        if (current < 0 || index > current)
        {
            return BibleTimelineState.Upcoming;
        }

        return index == current ? BibleTimelineState.Current : BibleTimelineState.Passed;
    }

    private static Dictionary<string, SceneLocation> LocateScenes(IReadOnlyList<BibleChapterInput> chapters)
    {
        Dictionary<string, SceneLocation> result = new(StringComparer.Ordinal);
        var line = 0;
        foreach (var chapter in chapters)
        {
            foreach (var scene in chapter.Scenes)
            {
                if (scene.Lines.Count == 0)
                {
                    continue;
                }

                result[scene.Key] = new SceneLocation(chapter.Name, chapter.Number, line + 1, line + scene.Lines.Count);
                line += scene.Lines.Count;
            }
        }

        return result;
    }

    private static string Owner(BibleScene? scene, BibleField field) => scene?.IsEdited(field) == true ? "Sua" : "IA";

    partial void OnNameTextChanged(string value) => Edit(BibleField.Name, s => s.EditName(value));

    partial void OnSummaryTextChanged(string value) => Edit(BibleField.Summary, s => s.EditSummary(value));

    partial void OnCharactersTextChanged(string value) => Edit(BibleField.Characters, s => s.EditCharacters(SplitList(value)));

    partial void OnToneTextChanged(string value) => Edit(BibleField.Tone, s => s.EditTone(SplitList(value)));

    partial void OnJokesTextChanged(string value) => Edit(BibleField.Jokes, s => s.EditJokes(SplitList(value)));

    partial void OnRelationsTextChanged(string value) => Edit(BibleField.Relations, s => s.EditRelations(ParseRelations(value)));

    partial void OnSynopsisTextChanged(string value)
    {
        if (loadingScene || project is not { } current || value == bible.Synopsis)
        {
            return;
        }

        bible = ProjectFolder.Open(current.Root).UpdateBible(b => b.EditSynopsis(value));
        OwnerOfSynopsis = "Sua";
    }

    private void Edit(BibleField field, Func<BibleScene, BibleScene> change)
    {
        if (loadingScene || project is not { } current || SelectedRow is not { } row)
        {
            return;
        }

        var before = bible.Scene(row.Key) ?? new BibleScene(row.Key);
        var after = change(before);
        if (after == before || (after with { Edited = before.Edited }) == before)
        {
            return;
        }

        bible = ProjectFolder.Open(current.Root).UpdateBible(b => b.WithScene(after));
        var index = allRows.FindIndex(r => r.Key == row.Key);
        var names = SafeNames(ProjectFolder.Open(current.Root));
        allRows[index] = RowOf(row.Key, row.Number, names);
        var shown = Rows.IndexOf(row);
        if (shown >= 0 && Filter == BibleFilter.All)
        {
            Rows[shown] = allRows[index];
            loadingScene = true;
            SelectedRow = allRows[index];
            loadingScene = false;
        }

        SetOwner(field, "Sua");
        OnPropertyChanged(nameof(EditedCount));
        OnPropertyChanged(nameof(NoSummaryCount));
        OnPropertyChanged(nameof(NamedCount));
    }

    private void SetOwner(BibleField field, string owner)
    {
        switch (field)
        {
            case BibleField.Name: OwnerOfName = owner; break;
            case BibleField.Summary: OwnerOfSummary = owner; break;
            case BibleField.Characters: OwnerOfCharacters = owner; break;
            case BibleField.Tone: OwnerOfTone = owner; break;
            case BibleField.Jokes: OwnerOfJokes = owner; break;
            case BibleField.Relations: OwnerOfRelations = owner; break;
        }
    }

    private static IEnumerable<string> SplitList(string text) =>
        text.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<BibleRelation> ParseRelations(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.IndexOf(':', StringComparison.Ordinal) is var at and > 0 ? new BibleRelation(l[..at], l[(at + 1)..]) : new BibleRelation(string.Empty, string.Empty));

    [RelayCommand]
    private void SetFilter(BibleFilter filter) => Filter = filter;

    /// <summary>Abre a janela "Gerar a bíblia do jogo": lê os capítulos e calcula o preço antes de qualquer chamada.</summary>
    [RelayCommand]
    private void OpenGenerate()
    {
        if (project is null)
        {
            return;
        }

        IsGenerateOpen = true;
        ProgressText = string.Empty;
        Generation = EstimateAsync();
    }

    [RelayCommand]
    private void CancelGenerate()
    {
        if (IsRunning)
        {
            run?.Cancel();
            return;
        }

        IsGenerateOpen = false;
    }

    private async Task EstimateAsync()
    {
        var current = project!;
        IsEstimating = true;
        EstimateText = "Calculando o preço...";
        try
        {
            var model = ModelOf(current);
            var effort = ClaudeEffortText.ParseOrDefault(current.Effort ?? "medium");
            planned = [];
            var (chapters, spentDollars) = await Task.Run(() =>
            {
                var folder = ProjectFolder.Open(current.Root);
                // O planejamento lê a bíblia sem reclamar de arquivo ilegível; gerar por cima dele pagaria a leitura e perderia o backup bom.
                folder.LoadBible();
                return (BibleChapterPlanner.Plan(folder), AiUsageLog.Load(folder).Dollars);
            }, CancellationToken.None).ConfigureAwait(true);
            planned = chapters;
            locations = LocateScenes(chapters);
            OnPropertyChanged(nameof(SceneHeader));
            RefreshTimeline();
            var estimate = BibleCost.Estimate(chapters, model, effort: effort);
            var rate = Rate();
            plannedBrl = estimate.Brl(rate);
            ReadText = $"{estimate.Lines.ToString("N0", BibleText.PtBr)} linhas · {estimate.Chapters} {(estimate.Chapters == 1 ? "capítulo" : "capítulos")}";
            EstimateText = $"Custo (aproximado): {VNForge.App.Controls.Money.Brl(estimate.Brl(rate))}";
            CostButtonText = VNForge.App.Controls.Money.Brl(estimate.Brl(rate));
            RereadText = $"cerca de {VNForge.App.Controls.Money.Brl(estimate.RereadBrl(rate))}";
            var kept = bible.Scenes.Count(s => s.Edited.Count > 0);
            EditedKeptText = $"Campos que você editou ({kept} {(kept == 1 ? "cena" : "cenas")})";
            var spent = rate.ToBrl(spentDollars);
            var limit = Limits().Limit;
            SpentText = limit is { } cap ? $"Já gasto no projeto: {VNForge.App.Controls.Money.Brl(spent)} de {VNForge.App.Controls.Money.Brl(cap)}" : $"Já gasto no projeto: {VNForge.App.Controls.Money.Brl(spent)}";
            SpentFraction = limit is > 0m ? (double)Math.Min(1m, spent / limit.Value) : 0;
            EstimateFraction = limit is > 0m ? (double)Math.Min(1m - (decimal)SpentFraction, estimate.Brl(rate) / limit.Value) : 0;
            OnPropertyChanged(nameof(RereadText));
            OnPropertyChanged(nameof(GenerateLabel));
        }
        catch (VNForgeException e)
        {
            ErrorMessage = e.Message;
            IsGenerateOpen = false;
        }
        finally
        {
            IsEstimating = false;
        }
    }

    /// <summary>A cena de exemplo usa a IA de demonstração, sem chave nem gasto.</summary>
    private IBibleProvider ProviderFor(ProjectSummary summary, string model, ClaudeEffort effort) =>
        summary.IsSample ? new FakeBibleProvider() : ProviderFactory!(model, effort);

    private static string ModelOf(ProjectSummary summary) => ClaudeModels.All.Contains(summary.Model) ? summary.Model : ClaudeModels.Opus;

    /// <summary>"Gerar a bíblia": lê os capítulos um por vez, grava cada cena assim que volta e mostra o andamento.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void ConfirmGenerate()
    {
        if (project is not { } current)
        {
            return;
        }

        if (ProviderFactory is null && !current.IsSample)
        {
            ErrorMessage = "Falta a chave de API. Configure-a nos Ajustes para gerar a bíblia.";
            IsGenerateOpen = false;
            return;
        }

        Generation = RunAsync(current);
    }

    private bool CanConfirm() => !IsEstimating && !IsRunning && planned.Count > 0;

    partial void OnIsEstimatingChanged(bool value) => ConfirmGenerateCommand.NotifyCanExecuteChanged();

    partial void OnIsRunningChanged(bool value) => ConfirmGenerateCommand.NotifyCanExecuteChanged();

    private async Task RunAsync(ProjectSummary current)
    {
        IsRunning = true;
        ErrorMessage = string.Empty;
        run = new CancellationTokenSource();
        var model = ModelOf(current);
        var effort = ClaudeEffortText.ParseOrDefault(current.Effort ?? "medium");
        var folder = ProjectFolder.Open(current.Root);
        try
        {
            var provider = ProviderFor(current, model, effort);
            var rate = Rate();
            var tracker = new RunCostTracker(folder, model, rate, () => (provider as ClaudeBibleProvider)?.Usage);
            var guard = tracker.CreateGuard(new BudgetLimits(LimitsFor(current).Limit, null), _ => plannedBrl, null);
            if (await RefuseAsync(guard, plannedBrl, LimitsFor(current).Limit, "Gerar a bíblia", run.Token).ConfigureAwait(true) is { } refusal)
            {
                ErrorMessage = refusal;
                return;
            }

            var runner = new BibleRunner(provider, new BibleRunOptions { Model = model, Budget = guard, RunDollars = tracker.RunDollars });
            var progress = new InlineProgress(p => ProgressText = $"Capítulo {Math.Min(p.ChaptersDone + 1, p.ChaptersTotal)} de {p.ChaptersTotal} · {p.Chapter} · {p.ScenesDone} de {p.ScenesTotal} cenas");
            BibleRunResult result;
            try
            {
                result = await runner.RunAsync(folder, planned, progress, run.Token).ConfigureAwait(true);
            }
            finally
            {
                guard.Release(plannedBrl);
                tracker.Record();
            }

            Reload();
            Notice = Summarize(result);
            if (result.Failures.Count > 0)
            {
                ErrorMessage = string.Join(" ", result.Failures.Select(f => $"'{f.Chapter}' não foi lido: {f.Error.Message}"));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ErrorMessage = Describe(e);
        }
        finally
        {
            IsRunning = false;
            IsGenerateOpen = false;
            run?.Dispose();
            run = null;
        }
    }

    private static string Summarize(BibleRunResult result)
    {
        var text = $"{result.ScenesWritten} {BibleText.Plural(result.ScenesWritten, "cena escrita", "cenas escritas")} em {result.ChaptersRead} {BibleText.Plural(result.ChaptersRead, "capítulo", "capítulos")}.";
        if (result.Canceled)
        {
            return text + " Cancelado: o que já foi escrito ficou na bíblia.";
        }

        return result.BudgetStop is not null ? text + " Parou no limite de gasto." : text;
    }

    /// <summary>"Reescrever com a IA" num campo da cena selecionada.</summary>
    [RelayCommand]
    private async Task RewriteField(BibleField field)
    {
        if (project is not { } current || SelectedRow is not { } row || (ProviderFactory is null && !project.IsSample))
        {
            ErrorMessage = ProviderFactory is null ? "Falta a chave de API. Configure-a nos Ajustes para reescrever com a IA." : string.Empty;
            return;
        }

        var model = ModelOf(current);
        var effort = ClaudeEffortText.ParseOrDefault(current.Effort ?? "medium");
        try
        {
            var folder = ProjectFolder.Open(current.Root);
            var chapter = await Task.Run(() =>
            {
                folder.LoadBible();
                return BibleChapterPlanner.Plan(folder).FirstOrDefault(c => c.Scenes.Any(s => s.Key == row.Key));
            }, CancellationToken.None).ConfigureAwait(true);
            if (chapter is null)
            {
                ErrorMessage = "Esta cena não tem texto para a IA ler.";
                return;
            }

            var provider = ProviderFor(current, model, effort);
            var rate = Rate();
            var tracker = new RunCostTracker(folder, model, rate, () => (provider as ClaudeBibleProvider)?.Usage);
            var limit = LimitsFor(current).Limit;
            var guard = tracker.CreateGuard(new BudgetLimits(limit, null), _ => 0m, null);

            // Uma chamada para uma cena: escreve o capítulo inteiro no cache e devolve só aquela cena.
            var estimate = BibleCost.EstimateRewrite(chapter, model, effort).Brl(rate);
            if (await RefuseAsync(guard, estimate, limit, "Reescrever com a IA", CancellationToken.None).ConfigureAwait(true) is { } refusal)
            {
                ErrorMessage = refusal;
                return;
            }

            try
            {
                await new BibleRunner(provider, new BibleRunOptions { Model = model }).RewriteFieldAsync(folder, chapter, row.Key, field, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                guard.Release(estimate);
                tracker.Record();
            }

            Reload();
        }
        catch (Exception e)
        {
            // O provedor pode lançar de tudo (rede, SDK, recusa); nada disso pode escapar do comando assíncrono.
            ErrorMessage = Describe(e);
        }
    }

    private BudgetLimits LimitsFor(ProjectSummary summary) => summary.IsSample ? BudgetLimits.None : Limits();

    /// <summary>Por que o pedido nem sai: o gasto já está no limite, ou o preço estimado passaria dele. Nulo quando cabe; quando cabe, deixa o valor reservado até o <see cref="BudgetGuard.Release"/>.</summary>
    private static async Task<string?> RefuseAsync(BudgetGuard guard, decimal estimateBrl, decimal? limit, string action, CancellationToken ct)
    {
        var cap = limit is { } value ? $" de {VNForge.App.Controls.Money.Brl(value)}" : string.Empty;
        if (await guard.AuthorizeAsync(estimateBrl, ct).ConfigureAwait(true) is not null)
        {
            return $"O gasto do projeto já chegou ao limite{cap}; nada foi enviado à IA. Aumente o limite nos Ajustes para continuar.";
        }

        return guard.TryReserve(estimateBrl)
            ? null
            : $"{action} custa cerca de {VNForge.App.Controls.Money.Brl(estimateBrl)} e, com o que o projeto já gastou, passaria do limite{cap}; nada foi enviado à IA. Aumente o limite nos Ajustes para continuar.";
    }

    /// <summary>A mensagem em português de uma falha: as do projeto e do disco falam por si; as da IA (rede, SDK, recusa, resposta fora do formato) passam por <see cref="ClaudeErrorMessages"/>.</summary>
    private static string Describe(Exception error) =>
        error is VNForgeException or InvalidOperationException or IOException ? error.Message : ClaudeErrorMessages.Describe(error);

    [RelayCommand]
    private void Undo()
    {
        if (project is not { } current)
        {
            return;
        }

        try
        {
            Notice = ProjectFolder.Open(current.Root).RestorePreviousBible() ? "A bíblia voltou ao que era antes da última geração." : "Não há geração anterior a que voltar.";
            Reload();
        }
        catch (VNForgeException e)
        {
            ErrorMessage = e.Message;
        }
    }

    /// <summary>"Exportar": grava <c>biblia.md</c> na pasta do projeto.</summary>
    [RelayCommand]
    private void Export()
    {
        if (project is not { } current)
        {
            return;
        }

        var path = Path.Combine(current.Root, "biblia.md");
        File.WriteAllText(path, BibleMarkdown.Render(bible, current.Name), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Notice = $"Bíblia exportada em {path}.";
    }

    private sealed class InlineProgress(Action<BibleProgress> report) : IProgress<BibleProgress>
    {
        public void Report(BibleProgress value) => report(value);
    }
}
