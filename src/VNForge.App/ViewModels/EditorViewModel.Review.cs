using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O ponto de entrada do modo revisão e o registro de aprovações; a tela e as regras da sequência estão em <see cref="ReviewModeViewModel"/>.</summary>
public sealed partial class EditorViewModel
{
    private IApprovalLog? approvalLog;
    private string? approvalLogRoot;

    /// <summary>Abre o registro de aprovações da pasta do projeto; os testes trocam por um na memória. O padrão grava em <c>.cache/sessions.json</c>.</summary>
    public Func<string, IApprovalLog>? ApprovalLogFactory { get; init; }

    /// <summary>O preço em reais de "Outra sugestão" para a linha; nulo deixa o botão sem preço.</summary>
    public Func<TextUnit, string>? SuggestionPrice { get; init; }

    /// <summary>O modo revisão aberto; nulo no Editor normal. A tela do Editor mostra o modo revisão no lugar da grade enquanto houver um.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewing))]
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
    public void StartReview()
    {
        if (SelectedScript is null || lineEditor is null || !FlushPendingEdits())
        {
            return;
        }

        // A grade tem de conter todas as linhas da fila: o modo revisão seleciona uma a uma e o ListBox escondido não pode desfazer a seleção.
        SelectFilter(chips[0]);
        SelectedSpeaker = Speakers[0];

        Review?.Dispose();
        var queue = scriptLines.Where(l => l.Status == TranslationStatus.Machine).ToList();
        RefreshReviewedToday();
        Review = new ReviewModeViewModel(this, queue);
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

    /// <summary>Seleciona a linha na grade e abre o detalhe dela; falso quando a seleção foi recusada (a gravação da linha anterior falhou).</summary>
    internal bool ShowLine(LineViewModel line)
    {
        SelectedLine = line;
        return ReferenceEquals(SelectedLine, line);
    }

    /// <summary>Abre outro script no Editor; o modo revisão usa para ir à próxima cena.</summary>
    internal void OpenScript(ScriptItemViewModel script) => SelectedScript = script;

    /// <summary>O próximo script depois do aberto que ainda tem linhas da IA para revisar; nulo no último.</summary>
    internal ScriptItemViewModel? NextScriptToReview()
    {
        var index = SelectedScript is { } open ? Scripts.IndexOf(open) : -1;
        return index < 0 ? null : Scripts.Skip(index + 1).FirstOrDefault(s => s.Progress.Counts.Machine > 0);
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
        new(other.SpeakerLabel, other.SourcePlain, other.HasTranslation ? other.TranslationPlain : null);

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
