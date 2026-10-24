using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Search;

namespace VNForge.App.ViewModels;

/// <summary>
/// Uma linha da lista de resultados. <see cref="After"/> é nulo numa busca simples e traz o texto novo na prévia da substituição.
/// <see cref="Focus"/> é onde a ocorrência (ou a troca) começa: a lista mostra um trecho em volta dele, para o que mudou nunca ficar escondido num texto longo.
/// </summary>
public sealed record SearchResultRow(string Location, string Before, string? After, int Count, int Focus = 0)
{
    private const int ExcerptWidth = 140;
    private const int ExcerptLead = 40;

    public bool HasAfter => After is not null;

    public string CountText => Count == 1 ? "1 ocorrência" : $"{ProjectLabels.Number(Count)} ocorrências";

    public string BeforeExcerpt => Excerpt(Before, Focus);

    public string? AfterExcerpt => After is null ? null : Excerpt(After, Focus);

    /// <summary>O texto inteiro se cabe; senão uma janela de até 140 caracteres que começa um pouco antes de <paramref name="focus"/>, com reticências nas pontas cortadas.</summary>
    public static string Excerpt(string text, int focus)
    {
        if (text.Length <= ExcerptWidth)
        {
            return text;
        }

        var start = Math.Clamp(focus - ExcerptLead, 0, text.Length - 1);
        if (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start--;
        }

        var end = Math.Min(text.Length, start + ExcerptWidth);
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        return $"{(start > 0 ? "…" : string.Empty)}{text[start..end]}{(end < text.Length ? "…" : string.Empty)}";
    }
}

/// <summary>
/// Página Busca: procura em todos os scripts do projeto e substitui na tradução, com regex. A substituição tem duas etapas: a prévia lista cada linha alterada
/// sem gravar, e só <see cref="ApplyCommand"/> grava, exatamente a prévia mostrada. Qualquer mudança nos campos descarta a prévia.
/// </summary>
public sealed partial class SearchReplaceViewModel : ObservableObject
{
    private const int SkippedNotesLimit = 20;

    private readonly TimeProvider clock;
    private readonly Func<string, SearchOptions, CancellationToken, SearchResult> find;
    private ReplacePreview? preview;
    private CancellationTokenSource? cancellation;
    private int version;

    /// <param name="clock">Relógio do horário da origem <c>manual</c> das linhas gravadas; o padrão é o do sistema.</param>
    /// <param name="find">Como a busca roda (pasta do projeto, opções e token); o padrão é <see cref="ProjectSearch"/>. Os testes passam uma busca que espera o cancelamento.</param>
    public SearchReplaceViewModel(TimeProvider? clock = null, Func<string, SearchOptions, CancellationToken, SearchResult>? find = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.find = find ?? ((root, options, token) => ProjectSearch.Find(ProjectFolder.Open(root), options, token));
    }

    /// <summary>Disparado depois de gravar pelo menos uma linha (ou de uma gravação que falhou no meio), para o Editor reler o projeto.</summary>
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

    /// <summary>Só a tradução em português separa palavras; no original em japonês kana e kanji são todos letras, então a opção fica desligada ali.</summary>
    [ObservableProperty]
    public partial bool WholeWord { get; set; }

    /// <summary>Procura no original em vez de na tradução; a substituição e a palavra inteira ficam desligadas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReplace), nameof(CanWholeWord))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    public partial bool SearchSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(PreviewCommand), nameof(ApplyCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Verdadeiro enquanto roda uma busca ou prévia, que o botão Cancelar interrompe. A gravação não se cancela.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsCancellable { get; private set; }

    /// <summary>As linhas achadas (busca) ou que mudariam (prévia). É trocada por inteiro, nunca recebe <c>Add</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial IReadOnlyList<SearchResultRow> Results { get; private set; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>Uma frase por linha que a prévia deixou como está, com o script, o número e o motivo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkippedNotes))]
    public partial IReadOnlyList<string> SkippedNotes { get; private set; } = [];

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public bool CanEdit => Project is not null;

    public bool CanReplace => !SearchSource;

    public bool CanWholeWord => !SearchSource;

    public bool HasResults => Results.Count > 0;

    public bool HasSkippedNotes => SkippedNotes.Count > 0;

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

    partial void OnSearchSourceChanged(bool value)
    {
        if (value)
        {
            WholeWord = false;
        }

        Invalidate();
    }

    partial void OnProjectChanged(ProjectSummary? value) => Invalidate();

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task FindAsync()
    {
        var root = Project!.Root;
        var options = Options();
        await RunAsync(
            async token =>
            {
                var found = await Task.Run(() => find(root, options, token), token);
                return () =>
                {
                    Results = [.. found.Hits.Select(h => new SearchResultRow(Location(h.ScriptKey, h.Number), h.Text, null, h.Matches.Count, h.Matches[0].Start))];
                    Summary = DescribeFind(found);
                };
            },
            cancellable: true);
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        var root = Project!.Root;
        var options = Options();
        var replacement = Replacement;
        await RunAsync(
            async token =>
            {
                var computed = await Task.Run(() => ProjectSearch.PreviewReplace(ProjectFolder.Open(root), options, replacement, token), token);
                return () =>
                {
                    preview = computed;
                    Results = [.. computed.Items.Select(i => new SearchResultRow(Location(i.ScriptKey, i.Number), i.Before, i.After, i.Count, FirstDifference(i.Before, i.After)))];
                    Summary = DescribePreview(computed);
                    SkippedNotes = DescribeSkipped(computed);
                    OnPropertyChanged(nameof(HasPreview));
                    ApplyCommand.NotifyCanExecuteChanged();
                };
            },
            cancellable: true);
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
        await RunAsync(
            async token =>
            {
                result = await Task.Run(() => ProjectSearch.Apply(ProjectFolder.Open(root), planned, clock), token);
                return () =>
                {
                    preview = null;
                    Results = [];
                    SkippedNotes = [];
                    Summary = DescribeResult(result);
                    OnPropertyChanged(nameof(HasPreview));
                    ApplyCommand.NotifyCanExecuteChanged();
                };
            },
            cancellable: false);

        if (result is { Applied: > 0 })
        {
            Applied?.Invoke(this, result);
        }
        else if (result is null && ErrorMessage is not null)
        {
            // A gravação falhou no meio: scripts anteriores podem já ter sido gravados, então o Editor relê o projeto.
            ErrorMessage += " Alguns scripts podem já ter sido gravados; confira no Editor.";
            Applied?.Invoke(this, new ReplaceResult(0, []));
        }
    }

    [RelayCommand(CanExecute = nameof(IsCancellable))]
    private void Cancel() => cancellation?.Cancel();

    private SearchOptions Options() => new(Pattern, UseRegex, MatchCase, WholeWord && !SearchSource, SearchSource ? SearchField.Source : SearchField.Translation);

    /// <summary>Roda uma operação fora da thread da tela. O resultado só entra se nada nos campos mudou enquanto ela rodava.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<Action>> work, bool cancellable)
    {
        var started = ++version;
        ErrorMessage = null;
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsCancellable = cancellable;
        IsBusy = true;
        try
        {
            var publish = await work(source.Token);
            if (started == version)
            {
                publish();
            }
        }
        catch (OperationCanceledException)
        {
            if (started == version)
            {
                Clear();
                Summary = "Cancelado. Nada foi gravado.";
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            if (started == version)
            {
                Clear();
                ErrorMessage = e.Message;
            }
        }
        finally
        {
            IsBusy = false;
            IsCancellable = false;
            cancellation = null;
        }
    }

    /// <summary>Descarta a prévia e os resultados: eles pertencem à busca anterior.</summary>
    private void Invalidate()
    {
        version++;
        Clear();
        ErrorMessage = null;
    }

    private void Clear()
    {
        preview = null;
        Results = [];
        Summary = string.Empty;
        SkippedNotes = [];
        OnPropertyChanged(nameof(HasPreview));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private static string DescribeFind(SearchResult found)
    {
        if (found.Hits.Count > 0)
        {
            return $"{Plural(found.Hits.Count, "linha", "linhas")} com {Plural(found.Hits.Sum(h => h.Matches.Count), "ocorrência", "ocorrências")}.";
        }

        return found.EmptyMatchLines > 0 ? EmptyOnly(found.EmptyMatchLines) : "Nenhuma ocorrência.";
    }

    private static string DescribePreview(ReplacePreview computed)
    {
        var parts = new List<string> { DescribeOutcome(computed) };

        if (computed.ApprovedLines > 0 || computed.ReviewedLines > 0)
        {
            var lost = new List<string>();
            if (computed.ApprovedLines > 0)
            {
                lost.Add(Plural(computed.ApprovedLines, "aprovada", "aprovadas"));
            }

            if (computed.ReviewedLines > 0)
            {
                lost.Add(Plural(computed.ReviewedLines, "revisada", "revisadas"));
            }

            parts.Add($"Voltam para Traduzida e precisam de nova revisão: {string.Join(" e ", lost)}.");
        }

        if (computed.ProtectedMatches > 0)
        {
            parts.Add($"Ocorrências em marcadores de controle deixadas de fora: {ProjectLabels.Number(computed.ProtectedMatches)}.");
        }

        if (computed.MalformedLines > 0)
        {
            parts.Add($"Linhas deixadas como estão porque o texto delas já tem um marcador de controle malformado: {ProjectLabels.Number(computed.MalformedLines)}.");
        }

        if (computed.BrokenByReplacementLines > 0)
        {
            parts.Add($"Linhas deixadas como estão porque a troca deixaria um marcador de controle malformado: {ProjectLabels.Number(computed.BrokenByReplacementLines)}.");
        }

        return string.Join(' ', parts);
    }

    private static string DescribeOutcome(ReplacePreview computed)
    {
        if (!computed.IsEmpty)
        {
            return $"A substituição altera {Plural(computed.Items.Count, "linha", "linhas")} ({Plural(computed.TotalReplacements, "ocorrência", "ocorrências")}). Nada foi gravado ainda.";
        }

        return computed.EmptyMatchLines > 0 && computed.SkippedLines == 0 ? EmptyOnly(computed.EmptyMatchLines) : "Nenhuma linha muda.";
    }

    private static List<string> DescribeSkipped(ReplacePreview computed)
    {
        var notes = computed.Skipped
            .Take(SkippedNotesLimit)
            .Select(s => $"{Location(s.ScriptKey, s.Number)}: {(s.Reason == SkipReason.MalformedMarkers ? "o texto já tem um marcador malformado" : "a troca deixaria um marcador malformado")}.")
            .ToList();
        if (computed.Skipped.Count > SkippedNotesLimit)
        {
            notes.Add($"E mais {ProjectLabels.Number(computed.Skipped.Count - SkippedNotesLimit)} linhas.");
        }

        return notes;
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

    private static string EmptyOnly(int lines) =>
        $"O padrão só casou texto vazio em {Plural(lines, "linha", "linhas")} (como ^, $ ou uma verificação de vizinhança), e a busca ignora ocorrência vazia. Inclua no padrão o texto que quer achar.";

    private static int FirstDifference(string before, string after)
    {
        var limit = Math.Min(before.Length, after.Length);
        var i = 0;
        while (i < limit && before[i] == after[i])
        {
            i++;
        }

        return i;
    }

    private static string Location(string scriptKey, int number) => $"{scriptKey} · linha {ProjectLabels.Number(number)}";

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{ProjectLabels.Number(count)} {many}";
}
