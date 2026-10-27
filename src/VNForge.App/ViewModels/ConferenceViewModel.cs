using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Pedido para abrir uma linha no Editor: o script e o id da linha (<c>yst00012:000034</c>).</summary>
public sealed record LineRequest(string ScriptKey, string UnitId);

/// <summary>
/// Tela "Conferir a tradução": o resultado do <see cref="QaEngine"/> do Core (o mesmo de <c>vnf check</c>) como "o que me impede de testar no jogo".
/// A faixa de cima separa o que trava o teste (erros) do que não trava (avisos); os filtros em linguagem comum (<see cref="ConferenceFilterKind"/>)
/// contam o projeto inteiro e só a lista muda com eles. Cada problema diz a cena e a linha, e o painel traz "O que é isso", o original, a tradução de hoje
/// e como vai ficar, com a correção proposta pelos <see cref="IQaFixer"/>. Salto até a linha no Editor, "Corrigir N com um clique" (<see cref="QaFixer"/>)
/// e "Está certo assim" (<see cref="QaFlags.MarkOk"/>). O projeto é lido quando a página é aberta (<see cref="RefreshAsync"/>); a lista é trocada por inteiro a cada filtro.
/// </summary>
public sealed partial class ConferenceViewModel : ObservableObject
{
    private readonly QaEngine engine;
    private readonly TimeProvider clock;
    private readonly ConferenceFilterViewModel[] filters;
    private QaReport report = QaReport.Empty;
    private IReadOnlyList<ConferenceIssueRowViewModel> all = [];
    private IReadOnlyList<ConferenceSettingCardViewModel> allCards = [];
    private ConferenceFilterKind activeFilter = ConferenceFilterKind.All;
    private int loadVersion;
    private int lineCount;
    private int fixableCount;
    private DateTimeOffset? checkedAt;

    public ConferenceViewModel(QaEngine? qa = null, TimeProvider? clock = null)
    {
        engine = qa ?? QaEngine.Default;
        this.clock = clock ?? TimeProvider.System;
        filters =
        [
            new ConferenceFilterViewModel(ConferenceFilterKind.All, "Todos") { IsSelected = true },
            new ConferenceFilterViewModel(ConferenceFilterKind.Blocking, "Travam o teste"),
            new ConferenceFilterViewModel(ConferenceFilterKind.OneClick, "Um clique"),
            new ConferenceFilterViewModel(ConferenceFilterKind.Box, "Não cabe na caixa"),
            new ConferenceFilterViewModel(ConferenceFilterKind.WordsAndNames, "Palavras e nomes"),
            new ConferenceFilterViewModel(ConferenceFilterKind.Accents, "Acentos"),
        ];
    }

    /// <summary>O usuário pediu para abrir uma linha; quem escuta (a janela principal) leva ao Editor.</summary>
    public event EventHandler<LineRequest>? LineRequested;

    /// <summary>O <c>formatting.json</c> foi alterado aqui (o botão do card do ajuste, ou "Corrigir com um clique"); Ajustes e o Editor precisam reler.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Um <c>text/*.json</c> foi alterado aqui (correção ou "ok"); o Editor precisa reler.</summary>
    public event EventHandler? TextsChanged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand))]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(ApplyFixCommand), nameof(MarkOkCommand), nameof(OpenLineCommand), nameof(OpenLineByIdCommand), nameof(FixAndNextCommand), nameof(ApplySettingCommand))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(IsBusy), nameof(IsClear), nameof(ShowEmptyText))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Verdadeiro enquanto uma correção ou um "ok" grava <c>text/*.json</c> em segundo plano.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixAllCommand), nameof(ApplyFixCommand), nameof(MarkOkCommand), nameof(OpenLineCommand), nameof(OpenLineByIdCommand), nameof(FixAndNextCommand), nameof(RefreshCommand), nameof(ApplySettingCommand))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsFixing { get; private set; }

    /// <summary>Lendo ou corrigindo o projeto. Nesse tempo nada mais grava em <c>text/*.json</c>, e a janela principal não deixa trocar de página.</summary>
    public bool IsBusy => IsLoading || IsFixing;

    /// <summary>Falha ao ler ou corrigir o projeto: o fato e a consequência, em português.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage), nameof(IsClear), nameof(ShowEmptyText))]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Resultado da última correção ("2 correções aplicadas.").</summary>
    [ObservableProperty]
    public partial string? NoticeMessage { get; private set; }

    /// <summary>Os filtros da faixa de cima, com os totais do projeto inteiro.</summary>
    public IReadOnlyList<ConferenceFilterViewModel> Filters => filters;

    /// <summary>
    /// Os problemas à vista, com o filtro escolhido: erros primeiro, depois por script e linha, e a "não traduzida" por último,
    /// um resumo por script. É trocada por inteiro; nunca recebe <c>Add</c> nem <c>Remove</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(EmptyMessage))]
    public partial IReadOnlyList<ConferenceIssueRowViewModel> Rows { get; private set; } = [];

    /// <summary>
    /// Os cards dos ajustes de formatação que a estratégia de acentuação não grava, no topo da lista: um por ajuste, no lugar das linhas que ele causa.
    /// Só os que o filtro escolhido inclui; é trocada por inteiro.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingCards), nameof(ShowEmptyText))]
    public partial IReadOnlyList<ConferenceSettingCardViewModel> SettingCards { get; private set; } = [];

    public bool HasSettingCards => SettingCards.Count > 0;

    /// <summary>O problema aberto no detalhe. Ao sair da lista (corrigido, silenciado ou filtrado), passa para o vizinho.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(FixAndNextCommand))]
    public partial ConferenceIssueRowViewModel? SelectedRow { get; set; }

    public bool HasSelection => SelectedRow is not null;

    /// <summary>"Última conferência há 4 min · 48.211 linhas · 14 regras ativas"; "Ainda não conferido" até a primeira leitura.</summary>
    [ObservableProperty]
    public partial string CheckedSummary { get; private set; } = string.Empty;

    /// <summary>"Mostrando 39 de 18.359 problemas abertos · agrupados por script", mais "3 avisos marcados como certos." quando há.</summary>
    [ObservableProperty]
    public partial string ListFooter { get; private set; } = string.Empty;

    /// <summary>"3 avisos marcados como certos."; vazio quando nenhum aviso foi silenciado.</summary>
    [ObservableProperty]
    public partial string SilencedSummary { get; private set; } = string.Empty;

    /// <summary>"2 problemas travam o teste no jogo."; vazio sem erro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockingErrors))]
    public partial string BlockingMessage { get; private set; } = string.Empty;

    /// <summary>O número da faixa do alto, que ela pinta de vermelho: "4"; vazio sem erro.</summary>
    [ObservableProperty]
    public partial string BlockingCount { get; private set; } = string.Empty;

    /// <summary>O resto da frase da faixa depois do número: "problemas travam o teste no jogo."; vazio sem erro.</summary>
    [ObservableProperty]
    public partial string BlockingRest { get; private set; } = string.Empty;

    /// <summary>Há erro aberto: a faixa do alto aparece. Erro nunca se silencia.</summary>
    public bool HasBlockingErrors => BlockingMessage.Length > 0;

    /// <summary>Há aviso e nenhum erro: a faixa do topo diz que nada trava o teste no jogo.</summary>
    public bool HasOnlyWarnings => !HasBlockingErrors && IssueCount is > 0;

    /// <summary>"Corrigir 3 com um clique"; "Corrigir com um clique" quando não há o que corrigir.</summary>
    [ObservableProperty]
    public partial string FixAllLabel { get; private set; } = "Corrigir com um clique";

    /// <summary>Há correção de um clique para aplicar: "Corrigir N com um clique" é o botão de destaque do cabeçalho; sem ela, o de destaque é "Conferir de novo".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIsPrimary))]
    public partial bool HasFixable { get; private set; }

    public bool RefreshIsPrimary => !HasFixable;

    /// <summary>Quantas ocorrências a última conferência achou, sem filtro; nulo enquanto o projeto não foi conferido (ou a conferência falhou).</summary>
    [ObservableProperty]
    public partial int? IssueCount { get; private set; }

    /// <summary>Quantas dessas ocorrências são erros, os que travam o teste no jogo (o filtro "Travam o teste"); nulo como <see cref="IssueCount"/>.</summary>
    [ObservableProperty]
    public partial int? ErrorCount { get; private set; }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>A frase de <see cref="EmptyMessage"/> aparece quando a lista está vazia e o estado vazio do <see cref="IsClear"/> não a substitui.</summary>
    public bool ShowEmptyText => IsEmpty && !IsClear && !HasSettingCards;

    /// <summary>A conferência rodou e não achou nada: a página mostra o estado vazio com o selo 訳.</summary>
    public bool IsClear => checkedAt is not null && Project is not null && !IsLoading && ErrorMessage is null && all.Count == 0;

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

    private static string DescribeSilenced(int count)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var phrase = count == 1 ? "aviso marcado como certo" : "avisos marcados como certos";
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

    private TaskCompletionSource? deferredRefresh;

    /// <summary>
    /// Os textos mudaram fora desta página (uma tradução em lote terminou): lê de novo. Uma leitura em curso não impede, porque a mais nova descarta
    /// a anterior; uma correção gravando impede, então a releitura espera ela acabar. O resultado só chega quando a releitura termina.
    /// </summary>
    public Task RefreshAfterExternalChangeAsync()
    {
        if (!IsFixing)
        {
            return RefreshAsync();
        }

        return (deferredRefresh ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    partial void OnIsFixingChanged(bool value)
    {
        if (!value && deferredRefresh is { } pending)
        {
            deferredRefresh = null;
            _ = RefreshThenCompleteAsync(pending);
        }
    }

    private async Task RefreshThenCompleteAsync(TaskCompletionSource pending)
    {
        try
        {
            await RefreshAsync();
        }
        finally
        {
            pending.SetResult();
        }
    }

    private void Load(Checked? loaded)
    {
        report = loaded?.Report ?? QaReport.Empty;
        all = loaded?.Rows ?? [];
        lineCount = loaded?.LineCount ?? 0;
        fixableCount = loaded?.Fixable ?? 0;
        allCards = loaded?.Cards ?? [];
        checkedAt = loaded is null ? null : clock.GetUtcNow();
        Rebuild();
    }

    /// <summary>O que uma conferência devolve: o relatório, as linhas já com a correção proposta e os números do cabeçalho.</summary>
    private sealed record Checked(QaReport Report, IReadOnlyList<ConferenceIssueRowViewModel> Rows, int LineCount, int Fixable, IReadOnlyList<ConferenceSettingCardViewModel> Cards);

    private Checked Check(string root)
    {
        var folder = ProjectFolder.Open(root);
        var context = QaContext.FromProject(folder);
        var sceneOf = SceneLabeler(SceneNames(folder), context);
        var positions = LinePositions(context);
        var result = engine.Run(context);
        var proposals = QaFixer.Propose(context.Texts, engine, result.Issues, context).ToDictionary(p => (p.UnitId, p.RuleId));
        var conflicts = SettingConflicts(folder);
        var settingIssues = SettingIssues(result.Issues, conflicts);
        var inCards = settingIssues.Select(x => x.Issue).ToHashSet(ReferenceEqualityComparer.Instance);

        // "Não traduzida" é resumo por script: só a primeira linha de cada um vai para o detalhe, as outras ficam fora da memória da tela.
        var untranslated = result.Issues.Where(i => i.RuleId == UntranslatedQaRule.RuleId).GroupBy(i => i.ScriptKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var others = result.Issues.Where(i => i.RuleId != UntranslatedQaRule.RuleId && !inCards.Contains(i)).ToList();
        var wanted = others.Select(i => i.UnitId).Concat(untranslated.Select(g => g.First().UnitId)).ToHashSet(StringComparer.Ordinal);
        var units = UnitsById(context, wanted);
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
                    issue.RuleId == UnencodableCharacterRule.RuleId ? group.ToList() : null,
                    new ConferenceRowLabels(engine.Find(issue.RuleId)?.Description, sceneOf(issue.ScriptKey), positions.GetValueOrDefault(issue.UnitId)),
                    new ConferenceRowCharacterContext(context.Encoder, conflicts));
            })
            .OrderBy(r => r.IsError ? 0 : 1)
            .ThenBy(r => r.ScriptKey, StringComparer.Ordinal)
            .ThenBy(r => r.LineNumber ?? int.MaxValue)
            .ThenBy(r => r.UnitId, StringComparer.Ordinal)
            .ThenBy(r => order.GetValueOrDefault(r.RuleId, int.MaxValue))
            .ToList();

        rows.AddRange(untranslated.Select(group =>
        {
            var head = group.First();
            return new ConferenceIssueRowViewModel(head, null, units.GetValueOrDefault(head.UnitId), group.Count(), labels: new ConferenceRowLabels(engine.Find(head.RuleId)?.Description, sceneOf(head.ScriptKey), positions.GetValueOrDefault(head.UnitId)));
        }));

        // As linhas que um ajuste causa contam nos totais (travam o teste, e o botão de um clique as resolve), mas a lista mostra um card por ajuste.
        var cards = new List<ConferenceSettingCardViewModel>();
        foreach (var conflict in conflicts)
        {
            var settingRows = settingIssues
                .Where(x => x.Conflict == conflict)
                .Select(x => new ConferenceIssueRowViewModel(
                    x.Issue,
                    labels: new ConferenceRowLabels(engine.Find(x.Issue.RuleId)?.Description, sceneOf(x.Issue.ScriptKey), positions.GetValueOrDefault(x.Issue.UnitId)),
                    characters: new ConferenceRowCharacterContext(context.Encoder, conflicts, conflict.Field)))
                .ToList();
            if (settingRows.Count > 0)
            {
                cards.Add(new ConferenceSettingCardViewModel(conflict, context.Encoder?.Id ?? string.Empty, settingRows));
                rows.AddRange(settingRows);
            }
        }

        return new Checked(result, rows, context.Texts.Values.Sum(list => list.Count), proposals.Count + cards.Sum(c => c.Count), cards);
    }

    /// <summary>O nome da cena na tela. Sem nome, "Cena N" com N a posição do script entre todos do projeto (ordem de chave, de 1 em diante); script fora da lista fica só "Cena", sem inventar número.</summary>
    private static Func<string, string> SceneLabeler(ScriptNames scenes, QaContext context)
    {
        var sceneNumbers = context.Texts.Keys.Select((key, index) => (key, number: index + 1)).ToDictionary(x => x.key, x => x.number, StringComparer.Ordinal);
        return key =>
        {
            if (key == GameTitleText.UnitId)
            {
                return "Título do jogo";
            }

            if (scenes.DisplayName(key) is { } name && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return sceneNumbers.TryGetValue(key, out var number) ? $"Cena {number.ToString(CultureInfo.InvariantCulture)}" : "Cena";
        };
    }

    /// <summary>A posição de cada linha no script, de 1 em diante, pelo id da unidade.</summary>
    private static Dictionary<string, int?> LinePositions(QaContext context)
    {
        var positions = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var list in context.Texts.Values)
        {
            for (var i = 0; i < list.Count; i++)
            {
                positions.TryAdd(list[i].Id, i + 1);
            }
        }

        return positions;
    }

    /// <summary>Cada problema "ajuste em conflito" com o ajuste que o causa; o que não casa com nenhum fica de fora.</summary>
    private static List<(QaIssue Issue, FormattingConflict? Conflict)> SettingIssues(IEnumerable<QaIssue> issues, IReadOnlyList<FormattingConflict> conflicts) => issues
        .Where(i => i.RuleId == FormattingConflictRule.RuleId)
        .Select(i => (Issue: i, Conflict: conflicts.FirstOrDefault(c => i.Message.StartsWith($"O ajuste {c.Setting} ({c.CurrentLabel}) ", StringComparison.Ordinal))))
        .Where(x => x.Conflict is not null)
        .ToList();

    private static Dictionary<string, TextUnit> UnitsById(QaContext context, HashSet<string> wanted)
    {
        var units = new Dictionary<string, TextUnit>(StringComparer.Ordinal);
        foreach (var unit in context.Texts.Values.SelectMany(list => list).Where(u => wanted.Contains(u.Id)))
        {
            units.TryAdd(unit.Id, unit);
        }

        return units;
    }

    /// <summary>Os ajustes em conflito com a estratégia do projeto; sem eles (arquivo ilegível), as linhas da regra ficam na lista como qualquer erro.</summary>
    private static IReadOnlyList<FormattingConflict> SettingConflicts(ProjectFolder folder)
    {
        try
        {
            return QaFixer.FindSettingConflicts(folder);
        }
        catch (VNForgeException)
        {
            return [];
        }
    }

    /// <summary>Os nomes das cenas; um <c>scripts.json</c> ilegível não derruba a conferência, só deixa as linhas pelo id do script.</summary>
    private static ScriptNames SceneNames(ProjectFolder folder)
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

    private bool CanRefresh() => !IsFixing;

    private static string Reason(Exception e) => e.Message.TrimEnd('.', ' ');

    [RelayCommand]
    private void SelectFilter(ConferenceFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        if (filter.Kind == activeFilter)
        {
            // O chip é um botão de alternância: clicar no marcado o desmarca sozinho. Volta ao estado do filtro sem refazer a lista.
            SyncSelection();
            return;
        }

        activeFilter = filter.Kind;
        Rebuild();
    }

    [RelayCommand(CanExecute = nameof(CanOpenLine))]
    private void OpenLine(ConferenceIssueRowViewModel? row)
    {
        if (row is not null)
        {
            OpenLineById(new LineRequest(row.ScriptKey, row.UnitId));
        }
    }

    private bool CanOpenLine(ConferenceIssueRowViewModel? row) => !IsBusy && row is not null;

    /// <summary>O usuário clicou no id de uma linha citado numa mensagem da página (um erro, por exemplo). Como <see cref="OpenLineCommand"/>, não vale com a página ocupada.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenLineById))]
    private void OpenLineById(LineRequest? request)
    {
        if (request is not null)
        {
            LineRequested?.Invoke(this, request);
        }
    }

    private bool CanOpenLineById(LineRequest? request) => !IsBusy && request is not null;

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
                ErrorMessage = $"Não foi possível registrar o aviso como certo: {Reason(e)}. O aviso continua na lista.";
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

    /// <summary>
    /// "Corrigir e ir para o próximo" (<c>Enter</c>): aplica a correção proposta, ou divide a fala em páginas quando o problema é a caixa que não cabe, e a lista
    /// passa para o problema que ficou no lugar dele.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFixAndNext))]
    private async Task FixAndNextAsync()
    {
        if (SelectedRow is { HasProposal: true } row)
        {
            await ApplyFixAsync(row);
        }
        else if (SelectedFit is { } fit && fit.SplitCommand.CanExecute(null))
        {
            await fit.SplitCommand.ExecuteAsync(null);
        }
    }

    private bool CanFixAndNext() => !IsBusy && (SelectedRow is { HasProposal: true } || SelectedFit?.SplitCommand.CanExecute(null) == true);

    /// <summary>O botão do card ("Usar três pontos"): troca o ajuste pelo valor seguro no <c>formatting.json</c>, avisa o app e confere de novo.</summary>
    [RelayCommand(CanExecute = nameof(CanApplySetting))]
    private async Task ApplySettingAsync(ConferenceSettingCardViewModel? card)
    {
        var root = Project?.Root;
        if (card is null || root is null)
        {
            return;
        }

        ErrorMessage = null;
        NoticeMessage = null;
        IsFixing = true;
        try
        {
            FormattingConflict? applied = null;
            string? failure = null;
            try
            {
                applied = await Task.Run(() => QaFixer.ApplySetting(ProjectFolder.Open(root), card.Field));
            }
            catch (Exception e)
            {
                failure = $"Não foi possível trocar o ajuste: {Reason(e)}. O ajuste continua como estava.";
            }

            if (applied is not null)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }

            await RefreshAsync();
            if (failure is not null)
            {
                ErrorMessage = failure;
            }
            else if (ErrorMessage is null)
            {
                NoticeMessage = applied is null ? "O ajuste já não estava em conflito." : SettingConflictTexts.Changed(applied);
            }
        }
        finally
        {
            IsFixing = false;
        }
    }

    private bool CanApplySetting(ConferenceSettingCardViewModel? card) => !IsBusy && card is not null;

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
                failure = $"Não foi possível corrigir com um clique: {Reason(e)}. Os scripts já corrigidos antes da falha continuam gravados.";
            }

            // O Apply grava script a script: uma falha no meio deixa os anteriores no disco, então o Editor e a lista releem de qualquer jeito.
            if (result is null || result.Fixes.Count > 0)
            {
                TextsChanged?.Invoke(this, EventArgs.Empty);
            }

            if (result is null || result.SettingsFixed.Count > 0)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }

            await RefreshAsync();
            if (failure is not null)
            {
                ErrorMessage = failure;
            }
            else if (ErrorMessage is null && result is not null)
            {
                NoticeMessage = FixedNotice(result);
            }
        }
        finally
        {
            IsFixing = false;
        }
    }

    /// <summary>"3 correções aplicadas. O ajuste Reticências passou de Caractere … para Três pontos."; o ajuste vem depois do número de correções de texto.</summary>
    private static string FixedNotice(QaFixResult result)
    {
        List<string> sentences = [];
        switch (result.Fixes.Count)
        {
            case 0:
                break;
            case 1:
                sentences.Add("1 correção aplicada.");
                break;
            case var n:
                sentences.Add($"{ProjectLabels.Number(n)} correções aplicadas.");
                break;
        }

        sentences.AddRange(result.SettingsFixed.Select(SettingConflictTexts.Changed));
        return sentences.Count == 0 ? "Nada a corrigir com um clique." : string.Join(" ", sentences);
    }

    private bool CanFixAll() => Project is not null && !IsBusy;

    private void SyncSelection()
    {
        foreach (var filter in filters)
        {
            filter.IsSelected = filter.Kind == activeFilter;
        }
    }

    private void Rebuild()
    {
        // Os totais dos filtros e do cabeçalho são do projeto inteiro, não da lista à vista: trocar de filtro não os muda.
        foreach (var filter in filters)
        {
            // "Um clique" conta as correções (uma por linha e regra), as mesmas do botão "Corrigir N com um clique"; os outros contam as ocorrências.
            filter.Count = filter.Kind == ConferenceFilterKind.OneClick ? all.Count(filter.Passes) : all.Where(filter.Passes).Sum(r => r.Count);
        }

        ErrorCount = checkedAt is null ? null : filters[1].Count;
        IssueCount = checkedAt is null ? null : filters[0].Count;

        SyncSelection();

        var active = filters.Single(f => f.Kind == activeFilter);
        List<ConferenceIssueRowViewModel> shown = [.. all.Where(r => r.SettingField is null && active.Passes(r))];
        List<ConferenceSettingCardViewModel> cards = [.. allCards.Where(card => active.Passes(card.Rows[0]))];
        var cardLines = cards.Sum(card => card.Count);
        var previous = SelectedRow;
        var previousIndex = previous is null ? -1 : Rows.ToList().IndexOf(previous);
        Rows = shown;
        SettingCards = cards;
        SelectedRow = Reselect(shown, previous, previousIndex);

        SilencedSummary = DescribeSilenced(report.Silenced.Count);
        // "Um clique" conta correções (uma por linha e regra), como o filtro e o botão do cabeçalho; os outros contam ocorrências.
        var footer = activeFilter == ConferenceFilterKind.OneClick
            ? $"Mostrando {ProjectLabels.Number(shown.Count + cardLines)} de {ProjectLabels.Number(filters[2].Count)} correções de um clique · agrupadas por cena"
            : $"Mostrando {ProjectLabels.Number(shown.Sum(r => r.Count) + cardLines)} de {ProjectLabels.Number(filters[0].Count)} problemas abertos · agrupados por cena";
        ListFooter = SilencedSummary.Length > 0 ? $"{footer} · {SilencedSummary}" : footer;
        RebuildBlocking(filters[1].Count);
        HasFixable = fixableCount > 0;
        FixAllLabel = fixableCount > 0 ? $"Corrigir {ProjectLabels.Number(fixableCount)} com um clique" : "Corrigir com um clique";
        CheckedSummary = DescribeChecked();
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsClear));
        OnPropertyChanged(nameof(ShowEmptyText));
        OnPropertyChanged(nameof(HasOnlyWarnings));
    }

    private void RebuildBlocking(int errors)
    {
        BlockingMessage = errors switch
        {
            0 => string.Empty,
            1 => "1 problema trava o teste no jogo.",
            _ => $"{ProjectLabels.Number(errors)} problemas travam o teste no jogo.",
        };
        BlockingCount = errors == 0 ? string.Empty : ProjectLabels.Number(errors);
        BlockingRest = errors == 0 ? string.Empty : BlockingMessage[(BlockingCount.Length + 1)..];
    }

    private string DescribeChecked()
    {
        if (Project is null)
        {
            return string.Empty;
        }

        return checkedAt is { } at
            ? $"Última conferência {RelativeTime.Format(at, clock.GetLocalNow())} · {Plural(lineCount, "linha", "linhas")} · {Plural(report.RulesRun.Count, "regra ativa", "regras ativas")}"
            : "Ainda não conferido";
    }

    private static string Plural(int n, string one, string many) => $"{ProjectLabels.Number(n)} {(n == 1 ? one : many)}";

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
