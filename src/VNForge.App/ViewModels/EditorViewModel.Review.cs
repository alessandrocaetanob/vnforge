using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Glossary;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O ponto de entrada do modo revisão e o registro de aprovações; a tela e as regras da sequência estão em <see cref="ReviewModeViewModel"/>.</summary>
public sealed partial class EditorViewModel
{
    private List<(string Source, int Count)>? speakerCounts;
    private IApprovalLog? approvalLog;
    private string? approvalLogRoot;
    private int riskVersion;
    private CancellationTokenSource? riskCancel;

    /// <summary>Abre o registro de aprovações da pasta do projeto; os testes trocam por um na memória. O padrão grava em <c>.cache/sessions.json</c>.</summary>
    public Func<string, IApprovalLog>? ApprovalLogFactory { get; init; }

    /// <summary>O preço em reais de "Outra sugestão" para a linha; nulo deixa o botão sem preço.</summary>
    public Func<TextUnit, string>? SuggestionPrice { get; init; }

    /// <summary>O modo revisão aberto; nulo no Editor normal. A tela do Editor mostra o modo revisão no lugar da grade enquanto houver um.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewing), nameof(IsGridShown))]
    [NotifyCanExecuteChangedFor(nameof(CloseSearchCommand))]
    public partial ReviewModeViewModel? Review { get; private set; }

    public bool IsReviewing => Review is not null;

    /// <summary>Quantas linhas diferentes foram aprovadas hoje neste projeto, pelo relógio e o fuso do computador.</summary>
    [ObservableProperty]
    public partial int ReviewedToday { get; private set; }

    /// <summary>A pessoa pediu para definir o gênero do personagem da linha; a janela abre o Glossário nos personagens.</summary>
    public event EventHandler? DefineGenderRequested;

    /// <summary>Abre o modo revisão nas linhas da IA do script aberto, da primeira à última. Sem script ou sem como gravar, não faz nada.</summary>
    [RelayCommand]
    public void StartReview() => StartReviewOf(ReviewSource.OpenScene);

    /// <summary>
    /// Abre o modo revisão na fonte pedida: a cena aberta, as falas de um personagem em todas as cenas ou as dúvidas da cena. Sem script ou sem como gravar, não faz nada.
    /// A fila por risco não passa por aqui: lê arquivos e pontua o projeto todo, e por isso abre por <see cref="StartRiskReviewAsync"/>, longe da thread da tela.
    /// </summary>
    public void StartReviewOf(ReviewSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind == ReviewSourceKind.Risk)
        {
            throw new ArgumentException("A fila por risco abre por StartRiskReviewAsync.", nameof(source));
        }

        if (SelectedScript is null || lineEditor is null || data is null || !FlushPendingEdits())
        {
            return;
        }

        // Gravar a edição pendente pode ter encerrado a última dúvida: a fila se monta depois, e a das dúvidas vazia não abre.
        var queue = ReviewQueueOf(source);
        if (source.Kind == ReviewSourceKind.Doubts && queue.Count == 0)
        {
            return;
        }

        OpenReview(source, queue, null);
    }

    /// <summary>Uma fila por risco em montagem deixa de valer: outra revisão abriu, outro projeto carregou, ou a pessoa pediu a fila de novo.</summary>
    private void CancelRiskWork()
    {
        riskVersion++;
        riskCancel?.Cancel();
    }

    private void OpenReview(ReviewSource source, IReadOnlyList<ReviewItem> queue, RiskReview? risk)
    {
        CancelRiskWork();
        EndReading();

        // A grade tem de conter todas as linhas da fila: o modo revisão seleciona uma a uma e o ListBox escondido não pode desfazer a seleção.
        SelectFilter(chips[0]);
        SelectedSpeaker = Speakers[0];

        Review?.Dispose();
        RefreshReviewedToday();
        Review = new ReviewModeViewModel(this, queue, source, risk);
    }

    /// <summary>
    /// Abre o modo revisão na fila "o que mais precisa de você": todo o projeto, do maior risco ao menor. A fila é montada fora da thread da tela (lê
    /// <c>.cache/editor-suggestions.json</c> e <c>.cache/sessions.json</c> e pontua cada linha); a tela só tira uma cópia das linhas e, no fim, abre a revisão.
    /// Sem linhas esperando revisão, ou se a pessoa abriu outra revisão no meio do caminho, não abre nada.
    /// </summary>
    [RelayCommand]
    public async Task StartRiskReviewAsync()
    {
        if (SelectedScript is null || lineEditor is null || data is not { } lines || !FlushPendingEdits())
        {
            return;
        }

        var version = ++riskVersion;
        var snapshot = SnapshotLines(lines);
        var root = Project?.Root;
        var log = OpenApprovalLog();
        var now = clock.GetUtcNow();
        var zone = clock.LocalTimeZone;
        var previous = riskCancel;
        using var source = riskCancel = new CancellationTokenSource();
        if (previous is not null)
        {
            await previous.CancelAsync();
        }
        RiskReview risk;
        try
        {
            risk = await Task.Run(() => BuildRiskReview(snapshot, root, log, now, zone), source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(riskCancel, source))
            {
                riskCancel = null;
            }
        }

        if (version != riskVersion || !ReferenceEquals(data, lines) || risk.Items.Count == 0 || SelectedScript is null || lineEditor is null || !FlushPendingEdits())
        {
            return;
        }

        OpenReview(ReviewSource.Risk, risk.Items, risk);
    }

    /// <summary>Quantas linhas de risco alto há agora, contadas fora da thread da tela; nulo sem projeto aberto. É o número que a paleta mostra.</summary>
    public async Task<int?> CountHighRiskAsync(CancellationToken cancellationToken = default)
    {
        if (data is not { } lines)
        {
            return null;
        }

        var snapshot = SnapshotLines(lines);
        var root = Project?.Root;
        var queue = await Task.Run(() => RiskQueue.Build(snapshot, LoadEditorSuggestions(root)), cancellationToken);
        return queue.Count(RiskLevel.High);
    }

    /// <summary>
    /// Uma cópia das referências das linhas, na ordem da história: as <see cref="LineEntry"/> não mudam depois de criadas, mas a grade as troca por outras a cada gravação,
    /// então quem lê de outra thread lê a cópia, nunca <see cref="ProjectLines"/>.
    /// </summary>
    private static LineEntry[] SnapshotLines(ProjectLines lines) => [.. lines.Scripts.SelectMany(script => lines.LinesOf(script.Key))];

    private static RiskReview BuildRiskReview(IReadOnlyList<LineEntry> lines, string? root, IApprovalLog? log, DateTimeOffset now, TimeZoneInfo zone)
    {
        var suggestions = LoadEditorSuggestions(root);
        var queue = RiskQueue.Build(lines, suggestions);
        var pace = log?.PaceOf(queue.Lines.Count, now, zone) ?? ReviewPace.None;
        return new RiskReview(queue, pace, suggestions);
    }

    /// <summary>As sugestões da passada de editor (#532) em <c>.cache/editor-suggestions.json</c>; sem pasta de projeto, sem arquivo ou com ele ilegível, nenhuma.</summary>
    private static EditorSuggestionsFile LoadEditorSuggestions(string? root)
    {
        if (root is null)
        {
            return EditorSuggestionsFile.Empty;
        }

        try
        {
            return EditorSuggestionsFile.Load(ProjectFolder.Open(root));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return EditorSuggestionsFile.Empty;
        }
    }

    /// <summary>Os personagens que têm linhas da IA para revisar, na ordem em que aparecem na história.</summary>
    public IReadOnlyList<ReviewableSpeaker> ReviewableSpeakers
    {
        get
        {
            if (data is null)
            {
                return [];
            }

            // A paleta pede a lista a cada tecla: a contagem fica guardada até uma gravação ou uma releitura mudar os dados; o nome em português é de agora.
            speakerCounts ??= CountSpeakersAwaitingReview(data);
            return [.. speakerCounts.Select(c => new ReviewableSpeaker(c.Source, DisplayNameOf(c.Source), c.Count))];
        }
    }

    private List<(string Source, int Count)> CountSpeakersAwaitingReview(ProjectLines lines)
    {
        var counts = new Dictionary<string, int>(CharacterBook.NameComparer);
        var order = new List<string>();
        foreach (var line in Scripts.SelectMany(s => lines.LinesOf(s.Key)).Where(IsAwaitingReview))
        {
            var speaker = line.Unit.EffectiveSpeaker;
            if (string.IsNullOrEmpty(speaker))
            {
                continue;
            }

            if (!counts.TryGetValue(speaker, out var count))
            {
                order.Add(speaker);
            }

            counts[speaker] = count + 1;
        }

        return [.. order.Select(speaker => (speaker, counts[speaker]))];
    }

    /// <summary>O nome do personagem em português quando está cadastrado; senão o do texto.</summary>
    internal string DisplayNameOf(string speaker) =>
        context.Characters.Find(speaker)?.Target is { Length: > 0 } target ? target : speaker;

    private static bool IsAwaitingReview(LineEntry line) => line.Unit.Status == TranslationStatus.Machine;

    private List<ReviewItem> ReviewQueueOf(ReviewSource source)
    {
        IEnumerable<LineEntry> lines = source.Kind switch
        {
            ReviewSourceKind.OpenScene => entries.Where(IsAwaitingReview),
            ReviewSourceKind.Speaker => Scripts.SelectMany(s => data!.LinesOf(s.Key))
                .Where(l => IsAwaitingReview(l) && CharacterBook.NameComparer.Equals(l.Unit.EffectiveSpeaker ?? string.Empty, source.Speaker!)),
            ReviewSourceKind.Doubts => entries.Where(l => l.Unit.Doubt is not null),
            ReviewSourceKind.Risk => throw new InvalidOperationException("A fila por risco vem de BuildRiskReview."),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source.Kind, "Fonte de revisão desconhecida."),
        };

        return [.. lines.Select(l => new ReviewItem(l.ScriptKey, l.Number, l.Unit.Id))];
    }

    /// <summary>Abre o modo revisão só nas linhas com dúvida da IA do script aberto, na ordem do script. Sem dúvida no script, não faz nada.</summary>
    [RelayCommand]
    public void StartDoubtReview()
    {
        if (DoubtLines > 0)
        {
            StartReviewOf(ReviewSource.Doubts);
        }
    }

    /// <summary>Fecha o modo revisão e volta à grade com a linha de agora selecionada.</summary>
    [RelayCommand]
    public void StopReview()
    {
        if (Review is not { } review)
        {
            return;
        }

        Review = null;
        review.Dispose();
    }

    /// <summary>
    /// Relê os personagens e o glossário do projeto e refaz o painel da linha aberta, sem trocar de linha. O Glossário grava <c>characters.json</c> por fora do Editor
    /// (o gênero definido a partir do modo revisão), e o aviso da linha só some com o contexto novo.
    /// </summary>
    public void RefreshContext()
    {
        if (Project?.Root is not { } root || lineEditor is null)
        {
            return;
        }

        context = contextLoader(root);
        if (SelectedLine is not { } line || Detail is not { } panel || !panel.Flush())
        {
            return;
        }

        if (!ReferenceEquals(panel.Saved, line.Entry.Unit))
        {
            savedUnits[panel.Id] = panel.Saved;
        }

        panel.Dispose();
        Detail = CreateDetail(line);
    }

    internal void RequestDefineGender() => DefineGenderRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Abre a cena do item (se não for a aberta), seleciona a linha na grade e abre o detalhe dela; falso quando a cena não abre ou a seleção foi recusada
    /// (a gravação da linha anterior falhou).
    /// </summary>
    internal bool ShowReviewItem(ReviewItem item)
    {
        if (!string.Equals(SelectedScript?.Key, item.ScriptKey, StringComparison.Ordinal))
        {
            var script = Scripts.FirstOrDefault(s => string.Equals(s.Key, item.ScriptKey, StringComparison.Ordinal));
            if (script is null || !FlushPendingEdits())
            {
                return false;
            }

            SelectedScript = script;
        }

        var index = item.Number - 1;
        if (index < 0 || index >= scriptLines.Length || scriptLines[index].Id != item.Id)
        {
            return false;
        }

        SelectedLine = scriptLines[index];
        return ReferenceEquals(SelectedLine, scriptLines[index]);
    }

    /// <summary>A linha de um item da fila como está nos dados agora, com a gravação mais recente; nula quando o item já não existe.</summary>
    internal LineEntry? EntryOf(ReviewItem item)
    {
        var lines = data?.LinesOf(item.ScriptKey);
        var index = item.Number - 1;
        return lines is not null && index >= 0 && index < lines.Count && lines[index].Unit.Id == item.Id ? lines[index] : null;
    }

    /// <summary>Abre outro script no Editor; o modo revisão usa para ir à próxima cena.</summary>
    internal void OpenScript(ScriptItemViewModel script) => SelectedScript = script;

    /// <summary>O próximo script depois do aberto que ainda tem linhas da IA para revisar; nulo no último.</summary>
    internal ScriptItemViewModel? NextScriptToReview()
    {
        var index = SelectedScript is { } open ? Scripts.IndexOf(open) : -1;
        return index < 0 ? null : Scripts.Skip(index + 1).FirstOrDefault(s => s.Progress.Counts.Machine > 0);
    }

    /// <summary>A fala anterior e a seguinte de um item da fila na cena aberta; vazias quando a cena do item não é a aberta.</summary>
    internal (ContextLine? Previous, ContextLine? Next) NeighboursOf(ReviewItem item)
    {
        var index = item.Number - 1;
        return string.Equals(SelectedScript?.Key, item.ScriptKey, StringComparison.Ordinal) && index >= 0 && index < scriptLines.Length
            ? NeighboursOf(scriptLines[index])
            : (null, null);
    }

    /// <summary>A fala anterior e a seguinte de uma linha do script aberto, mesmo que o filtro da grade as esconda.</summary>
    internal (ContextLine? Previous, ContextLine? Next) NeighboursOf(LineViewModel line)
    {
        var index = line.Number - 1;
        if (index < 0 || index >= scriptLines.Length || !ReferenceEquals(scriptLines[index], line))
        {
            return (null, null);
        }

        return (index > 0 ? ContextOf(scriptLines[index - 1]) : null, index + 1 < scriptLines.Length ? ContextOf(scriptLines[index + 1]) : null);
    }

    private static ContextLine ContextOf(LineViewModel other) =>
        new(other.SpeakerLabel, other.SourcePlain, other.HasTranslation ? other.TranslationPlain : null, Color: other.SpeakerColor);

    /// <summary>Guarda a aprovação com a hora do relógio do Editor. Só a passagem para Aprovada conta; gravar de novo uma linha já aprovada não.</summary>
    private void RecordApproval(string scriptKey, TextUnit previous, TextUnit saved)
    {
        if (saved.Status != TranslationStatus.Approved || previous.Status == TranslationStatus.Approved || OpenApprovalLog() is not { } log)
        {
            return;
        }

        log.Record(scriptKey, saved.Id, clock.GetUtcNow());
        RefreshReviewedToday();
    }

    /// <summary>Uma aprovação desfeita sai do registro e da contagem de hoje.</summary>
    private void ForgetApprovals(LineSavedEventArgs undone)
    {
        if (OpenApprovalLog() is not { } log)
        {
            return;
        }

        foreach (var item in undone.IsBatch ? undone.Batch : [undone])
        {
            if (item.Saved.Status == TranslationStatus.Approved && item.Previous.Status != TranslationStatus.Approved)
            {
                log.Remove(item.ScriptKey, item.Saved.Id);
            }
        }

        RefreshReviewedToday();
    }

    private void RefreshReviewedToday() =>
        ReviewedToday = OpenApprovalLog()?.ReviewedOn(clock.GetLocalNow(), clock.LocalTimeZone) ?? 0;

    private IApprovalLog? OpenApprovalLog()
    {
        var root = Project?.Root;
        if (root is null)
        {
            return null;
        }

        if (approvalLog is null || approvalLogRoot != root)
        {
            approvalLog = (ApprovalLogFactory ?? (folder => ProjectApprovalLog.For(folder)))(root);
            approvalLogRoot = root;
        }

        return approvalLog;
    }
}
