using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

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

/// <summary>
/// Página Conferência: o resultado do <see cref="QaEngine"/> do Core (o mesmo de <c>vnf check</c>) em três colunas, como o artboard: as regras por
/// categoria (<see cref="QaCategories"/>), a lista de problemas (a "não traduzida" resumida por script) e o detalhe do selecionado, com a correção
/// proposta pelos <see cref="IQaFixer"/>. Filtro de gravidade, salto até a linha no Editor, "corrigir automaticamente" (<see cref="QaFixer"/>) e
/// "marcar como ok" (<see cref="QaFlags"/>). O projeto é lido quando a página é aberta (<see cref="RefreshAsync"/>); a lista é trocada por inteiro a cada filtro.
/// </summary>
public sealed partial class ConferenceViewModel : ObservableObject
{
    private readonly QaEngine engine;
    private readonly TimeProvider clock;
    private readonly SeverityChipViewModel[] chips;
    private readonly ConferenceCategoryViewModel[] categories;
    private QaReport report = QaReport.Empty;
    private IReadOnlyList<ConferenceIssueRowViewModel> all = [];
    private ConferenceSeverityFilter severityFilter = ConferenceSeverityFilter.All;
    private string? categoryFilter;
    private int loadVersion;
    private int lineCount;
    private int fixableCount;
    private DateTimeOffset? checkedAt;

    public ConferenceViewModel(QaEngine? qa = null, TimeProvider? clock = null)
    {
        engine = qa ?? QaEngine.Default;
        this.clock = clock ?? TimeProvider.System;
        chips =
        [
            new SeverityChipViewModel(ConferenceSeverityFilter.All, "Todos") { IsSelected = true },
            new SeverityChipViewModel(ConferenceSeverityFilter.Errors, "Erros"),
            new SeverityChipViewModel(ConferenceSeverityFilter.Warnings, "Avisos"),
        ];
        categories =
        [
            new ConferenceCategoryViewModel(null) { IsSelected = true },
            .. QaCategories.All.Select(c => new ConferenceCategoryViewModel(c) { IsVisible = IsAlwaysListed(c) }),
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
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(ApplyFixCommand), nameof(MarkOkCommand), nameof(OpenLineCommand))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(IsBusy))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Verdadeiro enquanto uma correção ou um "ok" grava <c>text/*.json</c> em segundo plano.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(ApplyFixCommand), nameof(MarkOkCommand), nameof(OpenLineCommand), nameof(RefreshCommand))]
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

    /// <summary>A coluna "Regras": "Todas" e as categorias, com a contagem de problemas abertos de cada uma.</summary>
    public IReadOnlyList<ConferenceCategoryViewModel> Categories => categories;

    /// <summary>
    /// Os problemas à vista, com o filtro de gravidade e a categoria: erros primeiro, depois por script e linha, e a "não traduzida" por último,
    /// um resumo por script. É trocada por inteiro; nunca recebe <c>Add</c> nem <c>Remove</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(EmptyMessage))]
    public partial IReadOnlyList<ConferenceIssueRowViewModel> Rows { get; private set; } = [];

    /// <summary>O problema aberto no detalhe. Ao sair da lista (corrigido, silenciado ou filtrado), passa para o vizinho.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ConferenceIssueRowViewModel? SelectedRow { get; set; }

    public bool HasSelection => SelectedRow is not null;

    /// <summary>"Última conferência há 4 min · 48.211 linhas · 14 regras ativas"; "Ainda não conferido" até a primeira leitura.</summary>
    [ObservableProperty]
    public partial string CheckedSummary { get; private set; } = string.Empty;

    /// <summary>"Mostrando 39 de 18.359 problemas abertos · agrupados por script", mais "3 avisos marcados como ok." quando há.</summary>
    [ObservableProperty]
    public partial string ListFooter { get; private set; } = string.Empty;

    /// <summary>"3 avisos marcados como ok."; vazio quando nenhum aviso foi silenciado.</summary>
    [ObservableProperty]
    public partial string SilencedSummary { get; private set; } = string.Empty;

    /// <summary>"2 erros bloqueiam o build."; vazio sem erro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockingErrors))]
    public partial string BlockingMessage { get; private set; } = string.Empty;

    /// <summary>Há erro aberto: a faixa vermelha do alto aparece. Erro nunca se silencia.</summary>
    public bool HasBlockingErrors => BlockingMessage.Length > 0;

    /// <summary>"Corrigir 3 automaticamente"; "Corrigir automaticamente" quando não há o que corrigir.</summary>
    [ObservableProperty]
    public partial string FixAllLabel { get; private set; } = "Corrigir automaticamente";

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

            return all.Count > 0 ? "Nenhum problema com este filtro." : "Nenhum problema encontrado.";
        }
    }

    private static bool IsAlwaysListed(string category) => category != QaCategories.Translation && category != QaCategories.Other;

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
        Load(null);
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
            Load(null);
            return;
        }

        IsLoading = true;
        Checked? loaded = null;
        string? error = null;
        try
        {
            loaded = await Task.Run(() => Check(root));
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
        Load(loaded);
    }

    private void Load(Checked? loaded)
    {
        report = loaded?.Report ?? QaReport.Empty;
        all = loaded?.Rows ?? [];
        lineCount = loaded?.LineCount ?? 0;
        fixableCount = loaded?.Fixable ?? 0;
        checkedAt = loaded is null ? null : clock.GetUtcNow();
        Rebuild();
    }

    /// <summary>O que uma conferência devolve: o relatório, as linhas já com a correção proposta e os números do cabeçalho.</summary>
    private sealed record Checked(QaReport Report, IReadOnlyList<ConferenceIssueRowViewModel> Rows, int LineCount, int Fixable);

    private Checked Check(string root)
    {
        var context = QaContext.FromProject(ProjectFolder.Open(root));
        var result = engine.Run(context);
        var proposals = QaFixer.Propose(context.Texts, engine, result.Issues).ToDictionary(p => (p.UnitId, p.RuleId));

        // "Não traduzida" é resumo por script: só a primeira linha de cada um vai para o detalhe, as outras ficam fora da memória da tela.
        var untranslated = result.Issues.Where(i => i.RuleId == UntranslatedQaRule.RuleId).GroupBy(i => i.ScriptKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var others = result.Issues.Where(i => i.RuleId != UntranslatedQaRule.RuleId).ToList();
        var wanted = others.Select(i => i.UnitId).Concat(untranslated.Select(g => g.First().UnitId)).ToHashSet(StringComparer.Ordinal);
        var units = new Dictionary<string, TextUnit>(StringComparer.Ordinal);
        foreach (var unit in context.Texts.Values.SelectMany(list => list).Where(u => wanted.Contains(u.Id)))
        {
            units.TryAdd(unit.Id, unit);
        }

        var order = engine.Rules.Select((rule, index) => (rule.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        // Uma linha com a mesma regra apontada mais de uma vez (dois espaços duplos) é um problema só na lista; a contagem guarda quantas ocorrências são.
        var rows = others
            .GroupBy(issue => (issue.UnitId, issue.RuleId))
            .Select(group =>
            {
                var issue = group.First();
                return new ConferenceIssueRowViewModel(
                    issue,
                    proposals.GetValueOrDefault((issue.UnitId, issue.RuleId)),
                    units.GetValueOrDefault(issue.UnitId),
                    group.Count(),
                    engine.Find(issue.RuleId)?.Description);
            })
            .OrderBy(r => r.IsError ? 0 : 1)
            .ThenBy(r => r.ScriptKey, StringComparer.Ordinal)
            .ThenBy(r => r.UnitId, StringComparer.Ordinal)
            .ThenBy(r => order.GetValueOrDefault(r.RuleId, int.MaxValue))
            .ToList();

        rows.AddRange(untranslated.Select(group =>
        {
            var head = group.First();
            return new ConferenceIssueRowViewModel(head, null, units.GetValueOrDefault(head.UnitId), group.Count(), engine.Find(head.RuleId)?.Description);
        }));

        return new Checked(result, rows, context.Texts.Values.Sum(list => list.Count), proposals.Count);
    }

    private bool CanRefresh() => !IsFixing;

    private static string Reason(Exception e) => e.Message.TrimEnd('.', ' ');

    [RelayCommand]
    private void SelectSeverity(SeverityChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        if (chip.Filter == severityFilter)
        {
            // O chip é um botão de alternância: clicar no marcado o desmarca sozinho. Volta ao estado do filtro sem refazer a lista.
            SyncSelection();
            return;
        }

        severityFilter = chip.Filter;
        Rebuild();
    }

    [RelayCommand]
    private void SelectCategory(ConferenceCategoryViewModel? category)
    {
        if (category is null || category.Category == categoryFilter)
        {
            return;
        }

        categoryFilter = category.Category;
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
            all = [.. all.Where(r => !IsSamePair(r.Issue, row))];
            if (row.HasProposal)
            {
                fixableCount--;
            }

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

    /// <summary>"Aplicar correção": a proposta do <see cref="IQaFixer"/> da regra, só para esta linha. Reconfere em seguida.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyFix))]
    private async Task ApplyFixAsync(ConferenceIssueRowViewModel? row)
    {
        var root = Project?.Root;
        if (row is not { HasProposal: true } || root is null)
        {
            return;
        }

        ErrorMessage = null;
        NoticeMessage = null;
        IsFixing = true;
        try
        {
            QaFix? applied = null;
            string? failure = null;
            try
            {
                applied = await Task.Run(() => QaFixer.ApplyOne(ProjectFolder.Open(root), engine, row.ScriptKey, row.UnitId, row.RuleId));
            }
            catch (Exception e)
            {
                failure = $"Não foi possível aplicar a correção: {Reason(e)}. A tradução continua como estava.";
            }

            if (applied is not null)
            {
                TextsChanged?.Invoke(this, EventArgs.Empty);
            }

            await RefreshAsync();
            if (failure is not null)
            {
                ErrorMessage = failure;
            }
            else if (ErrorMessage is null)
            {
                NoticeMessage = applied is null ? "A linha já não tinha o que corrigir." : "1 correção aplicada.";
            }
        }
        finally
        {
            IsFixing = false;
        }
    }

    private bool CanApplyFix(ConferenceIssueRowViewModel? row) => !IsBusy && row is { HasProposal: true };

    [RelayCommand(CanExecute = nameof(CanFixAll))]
    private async Task FixAllAsync()
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
                result = await Task.Run(() => QaFixer.Apply(ProjectFolder.Open(root), engine));
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

    private bool CanFixAll() => Project is not null && !IsBusy;

    private void SyncSelection()
    {
        foreach (var chip in chips)
        {
            chip.IsSelected = chip.Filter == severityFilter;
        }

        foreach (var category in categories)
        {
            category.IsSelected = category.Category == categoryFilter;
        }
    }

    private void Rebuild()
    {
        // As contagens dos chips e da coluna de regras são do relatório inteiro, não do filtro: o artboard as mantém ao trocar de chip ou de regra.
        chips[0].Count = all.Sum(r => r.Count);
        chips[1].Count = all.Where(r => r.IsError).Sum(r => r.Count);
        chips[2].Count = chips[0].Count - chips[1].Count;
        foreach (var category in categories)
        {
            var inCategory = category.IsAll ? all : [.. all.Where(r => r.Category == category.Category)];
            category.Count = inCategory.Sum(r => r.Count);
            category.ContainsError = inCategory.Any(r => r.IsError);
            category.IsVisible = category.IsAll || IsAlwaysListed(category.Category!) || category.Count > 0 || category.Category == categoryFilter;
        }

        SyncSelection();

        List<ConferenceIssueRowViewModel> shown = [.. all.Where(r => (categoryFilter is null || r.Category == categoryFilter) && PassesSeverity(r))];
        var previous = SelectedRow;
        var previousIndex = previous is null ? -1 : Rows.ToList().IndexOf(previous);
        Rows = shown;
        SelectedRow = Reselect(shown, previous, previousIndex);

        SilencedSummary = DescribeSilenced(report.Silenced.Count);
        var footer = $"Mostrando {ProjectLabels.Number(shown.Sum(r => r.Count))} de {ProjectLabels.Number(chips[0].Count)} problemas abertos · agrupados por script";
        ListFooter = SilencedSummary.Length > 0 ? $"{footer} · {SilencedSummary}" : footer;
        var errors = chips[1].Count;
        BlockingMessage = errors switch
        {
            0 => string.Empty,
            1 => "1 erro bloqueia o build.",
            _ => $"{ProjectLabels.Number(errors)} erros bloqueiam o build.",
        };
        FixAllLabel = fixableCount > 0 ? $"Corrigir {ProjectLabels.Number(fixableCount)} automaticamente" : "Corrigir automaticamente";
        CheckedSummary = Project is null
            ? string.Empty
            : checkedAt is { } at
                ? $"Última conferência {RelativeTime.Format(at, clock.GetLocalNow())} · {Plural(lineCount, "linha", "linhas")} · {Plural(report.RulesRun.Count, "regra ativa", "regras ativas")}"
                : "Ainda não conferido";
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private static string Plural(int n, string one, string many) => $"{ProjectLabels.Number(n)} {(n == 1 ? one : many)}";

    private bool PassesSeverity(ConferenceIssueRowViewModel row) => severityFilter switch
    {
        ConferenceSeverityFilter.Errors => row.IsError,
        ConferenceSeverityFilter.Warnings => !row.IsError,
        _ => true,
    };

    /// <summary>Mantém o problema aberto se ainda está na lista (mesma regra e linha); senão, o que ficou no lugar dele, ou o último.</summary>
    private static ConferenceIssueRowViewModel? Reselect(IReadOnlyList<ConferenceIssueRowViewModel> rows, ConferenceIssueRowViewModel? previous, int previousIndex)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        if (previous is not null)
        {
            var same = rows.FirstOrDefault(r => r.RuleId == previous.RuleId && r.UnitId == previous.UnitId && r.ScriptKey == previous.ScriptKey);
            if (same is not null)
            {
                return same;
            }
        }

        return rows[Math.Clamp(previousIndex, 0, rows.Count - 1)];
    }
}
