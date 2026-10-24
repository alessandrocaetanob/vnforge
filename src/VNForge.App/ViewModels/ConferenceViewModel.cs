using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

/// <summary>Pedido para abrir uma linha no Editor: o script e o id da linha (<c>yst00012:000034</c>).</summary>
public sealed record LineRequest(string ScriptKey, string UnitId);

/// <summary>Filtro de gravidade da Conferência. <see cref="All"/> e <see cref="Errors"/> equivalem a <c>vnf check</c> e <c>vnf check --severity error</c>.</summary>
public enum ConferenceSeverityFilter
{
    All,
    Errors,
    Warnings,
}

/// <summary>Um chip do filtro de gravidade: o rótulo, quantas ocorrências passam e se é o filtro ativo.</summary>
public sealed partial class SeverityChipViewModel(ConferenceSeverityFilter filter, string label) : ObservableObject
{
    public ConferenceSeverityFilter Filter { get; } = filter;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    public partial int Count { get; set; }

    public string CountLabel => ProjectLabels.Number(Count);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Uma linha da lista da Conferência: o cabeçalho de uma regra ou uma ocorrência dela.</summary>
public abstract class ConferenceRowViewModel;

/// <summary>Cabeçalho de um grupo: a regra, a frase que ela confere, quantas ocorrências tem e se sabe corrigir sozinha.</summary>
public sealed class ConferenceGroupRowViewModel(string ruleId, string description, int count, bool canFix) : ConferenceRowViewModel
{
    public string RuleId { get; } = ruleId;

    public string Description { get; } = description;

    public int Count { get; } = count;

    public string CountLabel => $"{ProjectLabels.Number(Count)} {(Count == 1 ? "ocorrência" : "ocorrências")}";

    public bool CanFix { get; } = canFix;
}

/// <summary>Uma ocorrência do QA. Só leitura: a lista pode ter milhares delas.</summary>
public sealed class ConferenceIssueRowViewModel(QaIssue issue) : ConferenceRowViewModel
{
    public QaIssue Issue { get; } = issue;

    public string RuleId => Issue.RuleId;

    public string UnitId => Issue.UnitId;

    public string ScriptKey => Issue.ScriptKey;

    public string Message => Issue.Message;

    public bool IsError => Issue.Severity == QaSeverity.Error;

    public string SeverityLabel => IsError ? "Erro" : "Aviso";

    /// <summary>Erro não se silencia: só o aviso pode ser marcado como ok.</summary>
    public bool CanMarkOk => !IsError;
}

/// <summary>
/// Página Conferência: o resultado do <see cref="QaEngine"/> do Core (o mesmo de <c>vnf check</c>) agrupado por regra, com filtro de gravidade,
/// salto até a linha no Editor, "corrigir automaticamente" (<see cref="QaFixer"/>) e "marcar como ok" (<see cref="QaFlags"/>).
/// A lista é lida quando a página é aberta (<see cref="RefreshAsync"/>) e é trocada por inteiro a cada filtro.
/// </summary>
public sealed partial class ConferenceViewModel : ObservableObject
{
    private readonly QaEngine engine;
    private readonly SeverityChipViewModel[] chips;
    private QaReport report = QaReport.Empty;
    private ConferenceSeverityFilter severityFilter = ConferenceSeverityFilter.All;
    private int loadVersion;

    public ConferenceViewModel(QaEngine? qa = null)
    {
        engine = qa ?? QaEngine.Default;
        chips =
        [
            new SeverityChipViewModel(ConferenceSeverityFilter.All, "Todos") { IsSelected = true },
            new SeverityChipViewModel(ConferenceSeverityFilter.Errors, "Erros"),
            new SeverityChipViewModel(ConferenceSeverityFilter.Warnings, "Avisos"),
        ];
    }

    /// <summary>O usuário pediu para abrir uma linha; quem escuta (a janela principal) leva ao Editor.</summary>
    public event EventHandler<LineRequest>? LineRequested;

    /// <summary>Um <c>text/*.json</c> foi alterado aqui (correção ou "ok"); o Editor precisa reler.</summary>
    public event EventHandler? TextsChanged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand))]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(FixRuleCommand), nameof(MarkOkCommand), nameof(OpenLineCommand))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(IsBusy))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Verdadeiro enquanto uma correção grava <c>text/*.json</c> em segundo plano.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(FixRuleCommand), nameof(MarkOkCommand), nameof(OpenLineCommand))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsFixing { get; private set; }

    /// <summary>Lendo ou corrigindo o projeto. Nesse tempo nada mais grava em <c>text/*.json</c>, e a janela principal não deixa trocar de página.</summary>
    public bool IsBusy => IsLoading || IsFixing;

    /// <summary>Falha ao ler ou corrigir o projeto: o fato e a consequência, em português.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Resultado da última correção ("2 correções aplicadas.").</summary>
    [ObservableProperty]
    public partial string? NoticeMessage { get; private set; }

    public IReadOnlyList<SeverityChipViewModel> SeverityFilters => chips;

    /// <summary>Cabeçalhos e ocorrências na ordem do <c>vnf check</c>. É trocada por inteiro; nunca recebe <c>Add</c> nem <c>Remove</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial IReadOnlyList<ConferenceRowViewModel> Rows { get; private set; } = [];

    /// <summary>"2 erros, 37 avisos", contando o que o filtro deixa à vista.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>"3 avisos marcados como ok."; vazio quando nenhum aviso foi silenciado.</summary>
    [ObservableProperty]
    public partial string SilencedSummary { get; private set; } = string.Empty;

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyMessage =>
        Project is null ? "Abra um projeto para conferir as traduções."
        : IsLoading ? "Conferindo as traduções..."
        : ErrorMessage is not null ? "Não há problemas para mostrar."
        : report.Issues.Count > 0 ? "Nenhum problema com este filtro."
        : "Nenhum problema encontrado.";

    partial void OnProjectChanged(ProjectSummary? value)
    {
        loadVersion++;
        IsLoading = false;
        ErrorMessage = null;
        NoticeMessage = null;
        report = QaReport.Empty;
        Rebuild();
    }

    /// <summary>Roda o QA do Core sobre o projeto, fora da thread da tela. Uma conferência mais nova descarta o resultado de uma mais antiga.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var root = Project?.Root;
        var version = ++loadVersion;
        ErrorMessage = null;
        NoticeMessage = null;

        if (root is null)
        {
            IsLoading = false;
            report = QaReport.Empty;
            Rebuild();
            return;
        }

        IsLoading = true;
        QaReport? loaded = null;
        string? error = null;
        try
        {
            loaded = await Task.Run(() => engine.Run(QaContext.FromProject(ProjectFolder.Open(root))));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            error = $"Não foi possível conferir o projeto: {e.Message.TrimEnd('.', ' ')}. A lista fica vazia.";
        }

        if (version != loadVersion)
        {
            return;
        }

        IsLoading = false;
        ErrorMessage = error;
        report = loaded ?? QaReport.Empty;
        Rebuild();
    }

    [RelayCommand]
    private void SelectSeverity(SeverityChipViewModel? chip)
    {
        if (chip is null || chip.Filter == severityFilter)
        {
            return;
        }

        severityFilter = chip.Filter;
        Rebuild();
    }

    [RelayCommand(CanExecute = nameof(CanOpenLine))]
    private void OpenLine(ConferenceIssueRowViewModel? row)
    {
        if (row is not null)
        {
            LineRequested?.Invoke(this, new LineRequest(row.ScriptKey, row.UnitId));
        }
    }

    private bool CanOpenLine(ConferenceIssueRowViewModel? row) => !IsBusy && row is not null;

    [RelayCommand(CanExecute = nameof(CanMarkOk))]
    private void MarkOk(ConferenceIssueRowViewModel? row)
    {
        if (row is null || Project is null)
        {
            return;
        }

        try
        {
            QaFlags.MarkOk(ProjectFolder.Open(Project.Root), engine, row.UnitId, row.RuleId);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível marcar como ok: {e.Message.TrimEnd('.', ' ')}. O aviso continua na lista.";
            return;
        }

        ErrorMessage = null;
        NoticeMessage = null;

        // O Core silencia todo aviso da mesma regra nesta linha; a lista acompanha sem reler o projeto inteiro.
        var silenced = report.Issues.Where(i => IsSamePair(i, row)).ToList();
        report = new QaReport([.. report.Issues.Where(i => !IsSamePair(i, row))], [.. report.Silenced, .. silenced], report.RulesRun);
        Rebuild();
        TextsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanMarkOk(ConferenceIssueRowViewModel? row) => !IsBusy && row is { CanMarkOk: true };

    private static bool IsSamePair(QaIssue issue, ConferenceIssueRowViewModel row) =>
        issue.Severity == QaSeverity.Warning
        && string.Equals(issue.RuleId, row.RuleId, StringComparison.Ordinal)
        && string.Equals(issue.UnitId, row.UnitId, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanFixAll))]
    private Task FixAllAsync() => FixAsync(null);

    [RelayCommand(CanExecute = nameof(CanFixAll))]
    private Task FixRuleAsync(ConferenceGroupRowViewModel? group) => group is { CanFix: true } ? FixAsync([group.RuleId]) : Task.CompletedTask;

    private bool CanFixAll() => Project is not null && !IsBusy;

    private async Task FixAsync(IReadOnlyCollection<string>? rules)
    {
        var root = Project?.Root;
        if (root is null)
        {
            return;
        }

        ErrorMessage = null;
        NoticeMessage = null;
        IsFixing = true;
        try
        {
            QaFixResult result;
            try
            {
                result = await Task.Run(() => QaFixer.Apply(ProjectFolder.Open(root), engine, rules));
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                ErrorMessage = $"Não foi possível corrigir automaticamente: {e.Message.TrimEnd('.', ' ')}.";
                return;
            }

            if (result.Fixes.Count > 0)
            {
                TextsChanged?.Invoke(this, EventArgs.Empty);
            }

            await RefreshAsync();
            if (ErrorMessage is null)
            {
                NoticeMessage = result.Fixes.Count switch
                {
                    0 => "Nada a corrigir automaticamente.",
                    1 => "1 correção aplicada.",
                    var n => $"{ProjectLabels.Number(n)} correções aplicadas.",
                };
            }
        }
        finally
        {
            IsFixing = false;
        }
    }

    private void Rebuild()
    {
        var all = report.Issues;
        chips[0].Count = all.Count;
        chips[1].Count = all.Count(i => i.Severity == QaSeverity.Error);
        chips[2].Count = all.Count - chips[1].Count;
        foreach (var chip in chips)
        {
            chip.IsSelected = chip.Filter == severityFilter;
        }

        var shown = severityFilter switch
        {
            ConferenceSeverityFilter.Errors => all.Where(i => i.Severity == QaSeverity.Error).ToList(),
            ConferenceSeverityFilter.Warnings => all.Where(i => i.Severity == QaSeverity.Warning).ToList(),
            _ => all,
        };

        var rows = new List<ConferenceRowViewModel>(shown.Count + 8);
        foreach (var group in shown.GroupBy(i => i.RuleId, StringComparer.Ordinal))
        {
            var rule = engine.Find(group.Key);
            rows.Add(new ConferenceGroupRowViewModel(group.Key, rule?.Description ?? group.Key, group.Count(), rule is IQaFixer));
            rows.AddRange(group.Select(issue => new ConferenceIssueRowViewModel(issue)));
        }

        Rows = rows;
        Summary = Project is null ? string.Empty : QaBlockedException.Describe(new QaReport(shown, [], []));
        var silenced = report.Silenced.Count;
        SilencedSummary = silenced == 0 ? string.Empty : $"{ProjectLabels.Number(silenced)} {(silenced == 1 ? "aviso marcado como ok" : "avisos marcados como ok")}.";
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
