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
public interface IConferenceRow
{
    /// <summary>Id da regra a que a linha pertence: o do cabeçalho ou o da ocorrência.</summary>
    string RuleId { get; }
}

/// <summary>Cabeçalho de um grupo: a regra, a frase que ela confere, quantas ocorrências tem e se sabe corrigir sozinha.</summary>
public sealed class ConferenceGroupRowViewModel(string ruleId, string description, int count, bool canFix) : IConferenceRow
{
    public string RuleId { get; } = ruleId;

    public string Description { get; } = description;

    public int Count { get; } = count;

    public string CountLabel => $"{ProjectLabels.Number(Count)} {(Count == 1 ? "ocorrência" : "ocorrências")}";

    public bool CanFix { get; } = canFix;
}

/// <summary>Uma ocorrência do QA. Só leitura: a lista pode ter milhares delas.</summary>
public sealed class ConferenceIssueRowViewModel(QaIssue issue) : IConferenceRow
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
    private bool reportLoaded;

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

    /// <summary>Verdadeiro enquanto uma correção ou um "ok" grava <c>text/*.json</c> em segundo plano.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(FixRuleCommand), nameof(MarkOkCommand), nameof(OpenLineCommand), nameof(RefreshCommand))]
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
    public partial IReadOnlyList<IConferenceRow> Rows { get; private set; } = [];

    /// <summary>"2 erros, 37 avisos", contando o que o filtro deixa à vista.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>"3 avisos marcados como ok."; vazio quando nenhum aviso foi silenciado.</summary>
    [ObservableProperty]
    public partial string SilencedSummary { get; private set; } = string.Empty;

    /// <summary>Quantas ocorrências a última conferência achou, sem filtro; nulo enquanto o projeto não foi conferido (ou a conferência falhou).</summary>
    [ObservableProperty]
    public partial int? IssueCount { get; private set; }

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyMessage
    {
        get
        {
            if (Project is null)
            {
                return "Abra um projeto para conferir as traduções.";
            }

            if (IsLoading)
            {
                return "Conferindo as traduções...";
            }

            if (ErrorMessage is not null)
            {
                return "Não há problemas para mostrar.";
            }

            return report.Issues.Count > 0 ? "Nenhum problema com este filtro." : "Nenhum problema encontrado.";
        }
    }

    private static string DescribeSilenced(int count)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var phrase = count == 1 ? "aviso marcado como ok" : "avisos marcados como ok";
        return $"{ProjectLabels.Number(count)} {phrase}.";
    }

    partial void OnProjectChanged(ProjectSummary? value)
    {
        loadVersion++;
        IsLoading = false;
        ErrorMessage = null;
        NoticeMessage = null;
        report = QaReport.Empty;
        reportLoaded = false;
        Rebuild();
    }

    /// <summary>Roda o QA do Core sobre o projeto, fora da thread da tela. Uma conferência mais nova descarta o resultado de uma mais antiga.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
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
            reportLoaded = false;
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
        catch (Exception e)
        {
            // Qualquer falha (arquivo ilegível, regra que lança) vira mensagem: a página não pode ficar presa em "ocupada".
            error = $"Não foi possível conferir o projeto: {Reason(e)}. A lista fica vazia.";
        }
        finally
        {
            if (version == loadVersion)
            {
                IsLoading = false;
            }
        }

        if (version != loadVersion)
        {
            return;
        }

        ErrorMessage = error;
        report = loaded ?? QaReport.Empty;
        reportLoaded = loaded is not null;
        Rebuild();
    }

    private bool CanRefresh() => !IsFixing;

    private static string Reason(Exception e) => e.Message.TrimEnd('.', ' ');

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
    private async Task MarkOkAsync(ConferenceIssueRowViewModel? row)
    {
        var root = Project?.Root;
        if (row is null || root is null)
        {
            return;
        }

        ErrorMessage = null;
        NoticeMessage = null;
        var version = loadVersion;
        IsFixing = true;
        try
        {
            try
            {
                // Regrava o script inteiro: fora da thread da tela, e com a página ocupada para nada mais gravar ao mesmo tempo.
                await Task.Run(() => QaFlags.MarkOk(ProjectFolder.Open(root), engine, row.UnitId, row.RuleId));
            }
            catch (Exception e)
            {
                ErrorMessage = $"Não foi possível marcar como ok: {Reason(e)}. O aviso continua na lista.";
                return;
            }

            TextsChanged?.Invoke(this, EventArgs.Empty);
            if (version != loadVersion)
            {
                return;
            }

            // O Core silencia cada aviso da mesma regra nesta linha; a lista acompanha sem reler o projeto inteiro.
            var silenced = report.Issues.Where(i => IsSamePair(i, row)).ToList();
            report = new QaReport([.. report.Issues.Where(i => !IsSamePair(i, row))], [.. report.Silenced, .. silenced], report.RulesRun);
            Rebuild();
        }
        finally
        {
            IsFixing = false;
        }
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
            QaFixResult? result = null;
            string? failure = null;
            try
            {
                result = await Task.Run(() => QaFixer.Apply(ProjectFolder.Open(root), engine, rules));
            }
            catch (Exception e)
            {
                failure = $"Não foi possível corrigir automaticamente: {Reason(e)}. Os scripts já corrigidos antes da falha continuam gravados.";
            }

            // O Apply grava script a script: uma falha no meio deixa os anteriores no disco, então o Editor e a lista releem de qualquer jeito.
            if (result is null || result.Fixes.Count > 0)
            {
                TextsChanged?.Invoke(this, EventArgs.Empty);
            }

            await RefreshAsync();
            if (failure is not null)
            {
                ErrorMessage = failure;
            }
            else if (ErrorMessage is null && result is not null)
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
        IssueCount = reportLoaded ? all.Count : null;
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

        var rows = new List<IConferenceRow>(shown.Count + 8);
        foreach (var group in shown.GroupBy(i => i.RuleId, StringComparer.Ordinal))
        {
            var rule = engine.Find(group.Key);
            rows.Add(new ConferenceGroupRowViewModel(group.Key, rule?.Description ?? group.Key, group.Count(), rule is IQaFixer));
            rows.AddRange(group.Select(issue => new ConferenceIssueRowViewModel(issue)));
        }

        Rows = rows;
        Summary = Project is null ? string.Empty : QaBlockedException.Describe(new QaReport(shown, [], []));
        var silenced = report.Silenced.Count;
        SilencedSummary = DescribeSilenced(silenced);
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
