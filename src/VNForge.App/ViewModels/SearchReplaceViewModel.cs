using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Search;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha da lista de resultados. <see cref="After"/> é nulo numa busca simples e traz o texto novo na prévia da substituição.</summary>
public sealed record SearchResultRow(string Location, string Before, string? After, int Count)
{
    public bool HasAfter => After is not null;

    public string CountText => Count == 1 ? "1 ocorrência" : $"{ProjectLabels.Number(Count)} ocorrências";
}

/// <summary>
/// Página Busca: procura em todos os scripts do projeto e substitui na tradução, com regex. A substituição tem duas etapas: a prévia lista cada linha alterada
/// sem gravar, e só <see cref="ApplyCommand"/> grava, exatamente a prévia mostrada. Qualquer mudança nos campos descarta a prévia.
/// </summary>
public sealed partial class SearchReplaceViewModel : ObservableObject
{
    private readonly TimeProvider clock;
    private ReplacePreview? preview;
    private int version;

    public SearchReplaceViewModel(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>Disparado depois de gravar pelo menos uma linha, para o Editor reler o projeto.</summary>
    public event EventHandler<ReplaceResult>? Applied;

    /// <summary>Grava o que o Editor ainda tem pendente antes de a substituição mexer nos arquivos; falso cancela a aplicação.</summary>
    public Func<bool>? FlushPendingEdits { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(PreviewCommand), nameof(ApplyCommand))]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(PreviewCommand))]
    public partial string Pattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Replacement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool UseRegex { get; set; }

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    [ObservableProperty]
    public partial bool WholeWord { get; set; }

    /// <summary>Procura no original em vez de na tradução; a substituição fica desligada, porque o original do jogo não se altera.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReplace))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    public partial bool SearchSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(PreviewCommand), nameof(ApplyCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>As linhas achadas (busca) ou que mudariam (prévia). É trocada por inteiro, nunca recebe <c>Add</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial IReadOnlyList<SearchResultRow> Results { get; private set; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public bool CanEdit => Project is not null;

    public bool CanReplace => !SearchSource;

    public bool HasResults => Results.Count > 0;

    /// <summary>Verdadeiro com uma prévia que altera alguma linha; só então o botão Aplicar vale.</summary>
    public bool HasPreview => preview is { IsEmpty: false };

    private bool CanSearch => Project is not null && !IsBusy && !string.IsNullOrEmpty(Pattern);

    private bool CanPreview => CanSearch && !SearchSource;

    private bool CanApplyPreview => Project is not null && !IsBusy && HasPreview;

    partial void OnPatternChanged(string value) => Invalidate();

    partial void OnReplacementChanged(string value) => Invalidate();

    partial void OnUseRegexChanged(bool value) => Invalidate();

    partial void OnMatchCaseChanged(bool value) => Invalidate();

    partial void OnWholeWordChanged(bool value) => Invalidate();

    partial void OnSearchSourceChanged(bool value) => Invalidate();

    partial void OnProjectChanged(ProjectSummary? value) => Invalidate();

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task FindAsync()
    {
        var root = Project!.Root;
        var options = Options();
        await RunAsync(async () =>
        {
            var hits = await Task.Run(() => ProjectSearch.Find(ProjectFolder.Open(root), options));
            return () =>
            {
                Results = [.. hits.Select(h => new SearchResultRow(Location(h.ScriptKey, h.Number), h.Text, null, h.Matches.Count))];
                Summary = hits.Count == 0
                    ? "Nenhuma ocorrência."
                    : $"{Plural(hits.Count, "linha", "linhas")} com {Plural(hits.Sum(h => h.Matches.Count), "ocorrência", "ocorrências")}.";
            };
        });
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        var root = Project!.Root;
        var options = Options();
        var replacement = Replacement;
        await RunAsync(async () =>
        {
            var computed = await Task.Run(() => ProjectSearch.PreviewReplace(ProjectFolder.Open(root), options, replacement));
            return () =>
            {
                preview = computed;
                Results = [.. computed.Items.Select(i => new SearchResultRow(Location(i.ScriptKey, i.Number), i.Before, i.After, i.Count))];
                Summary = DescribePreview(computed);
                OnPropertyChanged(nameof(HasPreview));
                ApplyCommand.NotifyCanExecuteChanged();
            };
        });
    }

    [RelayCommand(CanExecute = nameof(CanApplyPreview))]
    private async Task ApplyAsync()
    {
        var planned = preview;
        if (planned is null || Project is null)
        {
            return;
        }

        if (FlushPendingEdits?.Invoke() == false)
        {
            ErrorMessage = "A linha aberta no Editor tem uma edição que não foi gravada. Resolva no Editor e aplique de novo.";
            return;
        }

        var root = Project.Root;
        ReplaceResult? result = null;
        await RunAsync(async () =>
        {
            result = await Task.Run(() => ProjectSearch.Apply(ProjectFolder.Open(root), planned, clock));
            return () =>
            {
                preview = null;
                Results = [];
                Summary = DescribeResult(result);
                OnPropertyChanged(nameof(HasPreview));
                ApplyCommand.NotifyCanExecuteChanged();
            };
        });

        if (result is { Applied: > 0 })
        {
            Applied?.Invoke(this, result);
        }
    }

    private SearchOptions Options() => new(Pattern, UseRegex, MatchCase, WholeWord, SearchSource ? SearchField.Source : SearchField.Translation);

    /// <summary>Roda uma operação fora da thread da tela. O resultado só entra se nada nos campos mudou enquanto ela rodava.</summary>
    private async Task RunAsync(Func<Task<Action>> work)
    {
        var started = ++version;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var publish = await work();
            if (started == version)
            {
                publish();
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            if (started == version)
            {
                preview = null;
                Results = [];
                Summary = string.Empty;
                OnPropertyChanged(nameof(HasPreview));
                ErrorMessage = e.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Descarta a prévia e os resultados: eles pertencem à busca anterior.</summary>
    private void Invalidate()
    {
        version++;
        preview = null;
        Results = [];
        Summary = string.Empty;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasPreview));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private static string DescribePreview(ReplacePreview computed)
    {
        var parts = new List<string>();
        parts.Add(computed.IsEmpty
            ? "Nenhuma linha muda."
            : $"A substituição altera {Plural(computed.Items.Count, "linha", "linhas")} ({Plural(computed.TotalReplacements, "ocorrência", "ocorrências")}). Nada foi gravado ainda.");
        if (computed.ProtectedMatches > 0)
        {
            parts.Add($"Ocorrências em marcadores de controle deixadas de fora: {ProjectLabels.Number(computed.ProtectedMatches)}.");
        }

        if (computed.SkippedLines > 0)
        {
            parts.Add($"Linhas deixadas como estão porque a troca deixaria um marcador de controle malformado: {ProjectLabels.Number(computed.SkippedLines)}.");
        }

        return string.Join(' ', parts);
    }

    private static string DescribeResult(ReplaceResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        var text = result.Applied == 0 ? "Nenhuma linha foi gravada." : $"{Plural(result.Applied, "linha gravada", "linhas gravadas")}.";
        return result.Stale.Count == 0
            ? text
            : $"{text} {Plural(result.Stale.Count, "linha mudou", "linhas mudaram")} depois da prévia e não foi alterada. Gere a prévia de novo.";
    }

    private static string Location(string scriptKey, int number) => $"{scriptKey} · linha {ProjectLabels.Number(number)}";

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{ProjectLabels.Number(count)} {many}";
}
