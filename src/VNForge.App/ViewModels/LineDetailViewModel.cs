using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// Painel de detalhe de uma linha do Editor: original, tradução, notas e aprovação. Cada edição grava em <c>text/&lt;script&gt;.json</c> depois de
/// <see cref="SaveDelay"/> sem digitar; trocar de linha, aprovar e descartar o painel (<see cref="Dispose"/>) gravam antes o que estiver pendente.
/// </summary>
public sealed partial class LineDetailViewModel : ObservableObject, IDisposable
{
    /// <summary>Tempo sem digitar até a gravação (design do <c>TranslationField</c>).</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly TextUnit unit;
    private readonly string scriptKey;
    private readonly LineEditor editor;
    private readonly ILineNavigator navigator;
    private readonly TimeProvider clock;
    private readonly Action<Action> postToUi;
    private readonly IReadOnlyList<string> sourceMarkers;
    private ITimer? timer;
    private bool loading = true;
    private bool disposed;
    private bool translationDirty;
    private bool notesDirty;

    /// <summary>Usa o <see cref="SynchronizationContext"/> da thread que criou o painel (a da interface) para as gravações agendadas.</summary>
    public LineDetailViewModel(TextUnit unit, string scriptKey, LineEditor editor, ILineNavigator navigator, TimeProvider clock)
        : this(unit, scriptKey, editor, navigator, clock, CaptureContext())
    {
    }

    /// <param name="postToUi">Executa a gravação agendada na thread da interface; os testes passam uma chamada direta.</param>
    public LineDetailViewModel(TextUnit unit, string scriptKey, LineEditor editor, ILineNavigator navigator, TimeProvider clock, Action<Action> postToUi)
    {
        this.unit = unit ?? throw new ArgumentNullException(nameof(unit));
        this.scriptKey = scriptKey ?? throw new ArgumentNullException(nameof(scriptKey));
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));

        Saved = unit;
        SourceSegments = SplitOrPlain(unit.Source, out sourceMarkers);
        Translation = unit.Translation ?? string.Empty;
        Notes = unit.Notes;
        Status = unit.Status;
        loading = false;
        UpdateMarkers();
    }

    public string Id => unit.Id;

    /// <summary>A unidade como está gravada em <c>text/&lt;script&gt;.json</c>: a recebida na criação, trocada a cada gravação bem-sucedida.</summary>
    public TextUnit Saved
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged(nameof(HistoryText));
            SavedChanged?.Invoke(value);
        }
    }

    /// <summary>Disparado a cada gravação bem-sucedida, com a unidade como ficou no arquivo; o Editor usa para atualizar a linha da grade.</summary>
    public event Action<TextUnit>? SavedChanged;

    public string ScriptKey => scriptKey;

    public string? Speaker => string.IsNullOrEmpty(unit.Speaker) ? null : unit.Speaker;

    /// <summary>Personagem e glossário da linha, para o contexto do inspetor; nulo quando o Editor não os carregou.</summary>
    public LineContext? Context { get; init; }

    /// <summary>O id da linha no cabeçalho do inspetor ("yst00013:0043").</summary>
    public string FullId => unit.Id;

    public string KindLabel => unit.Kind switch
    {
        TextUnitKind.Dialogue => "Diálogo",
        TextUnitKind.Narration => "Narração",
        TextUnitKind.Name => "Nome",
        _ => unit.Kind.ToString(),
    };

    public bool HasSpeaker => Speaker is not null;

    /// <summary>De onde veio a tradução gravada: a IA e o modelo, ou uma pessoa, e quando. É o conteúdo do botão de histórico do cabeçalho.</summary>
    public string HistoryText
    {
        get
        {
            if (Saved.Origin is not { } origin)
            {
                return "Sem registro de origem para esta tradução.";
            }

            var age = RelativeTime.Format(origin.At, clock.GetUtcNow());
            return origin.Provider == "manual"
                ? $"Editada à mão ({age})."
                : origin.Model is { Length: > 0 } model
                    ? $"Traduzida pela IA, {ProjectLabels.Model(model)} ({age})."
                    : $"Traduzida pela IA ({age}).";
        }
    }

    /// <summary>Ctrl+Shift+Enter: copia o original para o campo de tradução, para linhas que ficam como estão.</summary>
    [RelayCommand]
    private void CopyOriginal() => Translation = Source;

    /// <summary>O nome do personagem em português, quando ele está cadastrado em <c>characters.json</c>.</summary>
    public string? SpeakerTarget => Context?.SpeakerTarget;

    /// <summary>"美咲 → Misaki" no alto do original; só o nome original sem cadastro; "Narração" sem falante.</summary>
    public string SpeakerPair => Speaker is null ? "Narração" : SpeakerTarget is null ? Speaker : $"{Speaker} → {SpeakerTarget}";

    /// <summary>O tom de voz do personagem, no bloco de contexto.</summary>
    public string? Voice => string.IsNullOrWhiteSpace(Context?.Voice) ? null : Context.Voice;

    public bool HasVoice => Voice is not null;

    /// <summary>O nome do personagem no bloco de contexto: o português quando cadastrado, o original quando não.</summary>
    public string SpeakerName => SpeakerTarget ?? Speaker ?? string.Empty;

    /// <summary>A voz do personagem depois do nome ("— seco e resmungão"); vazio sem voz cadastrada.</summary>
    public string VoiceText => Voice is null ? string.Empty : $" — {Voice}";

    /// <summary>Os termos do glossário que aparecem no original, ou a frase que diz que não há nenhum.</summary>
    public string GlossaryText
    {
        get
        {
            var terms = Context?.Glossary ?? [];
            return terms.Count == 0
                ? "Nenhum termo do glossário nesta linha."
                : $"Glossário: {string.Join(" · ", terms.Select(t => $"{t.Source} → {t.Target}"))}";
        }
    }

    /// <summary>Quantos caracteres a tradução tem, sem os marcadores de pausa ("38 caracteres").</summary>
    public string CharsText
    {
        get
        {
            var count = LineText.Length(Translation);
            return count == 1 ? "1 caractere" : $"{ProjectLabels.Number(count)} caracteres";
        }
    }

    /// <summary>Linhas e colunas da tradução na caixa do perfil ("2 de 3 linhas · 38/40 colunas"); vazio sem perfil legível.</summary>
    public string FitText
    {
        get
        {
            if (Preview is null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(Translation))
            {
                return "Sem tradução";
            }

            if (Preview.Layout is not { } layout)
            {
                return string.Empty;
            }

            var lines = layout.Boxes.Select(b => b.Lines.Count).DefaultIfEmpty().Max();
            var longest = layout.Boxes.SelectMany(b => b.Lines).Select(l => l.Columns).DefaultIfEmpty().Max();
            var linesPart = layout.MaxLines is { } max ? $"{lines} de {max} linhas" : lines == 1 ? "1 linha" : $"{lines} linhas";
            return $"{linesPart} · {longest}/{layout.Columns} colunas";
        }
    }

    /// <summary>Os marcadores da tradução são os do original (nenhum a menos, nenhum a mais); falso sem tradução.</summary>
    public bool MarkersMatch { get; private set; }

    public string MarkerStateText =>
        string.IsNullOrWhiteSpace(Translation) ? string.Empty
        : MarkersMatch ? "Marcadores iguais ao original"
        : "Marcadores diferentes do original";

    public string Source => unit.Source;

    /// <summary>O original em trechos de texto e marcadores, para mostrar os marcadores como chips.</summary>
    public IReadOnlyList<TextSegment> SourceSegments { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharsText))]
    public partial string Translation { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsApproved))]
    public partial TranslationStatus Status { get; set; }

    /// <summary>Marcadores do original que a tradução não tem (cada ocorrência que falta conta uma vez).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingMarkers))]
    [NotifyPropertyChangedFor(nameof(MissingMarkersText))]
    public partial IReadOnlyList<string> MissingMarkers { get; set; } = [];

    /// <summary>Por que não foi possível ler os marcadores da tradução (chave sem fechamento, marcador inválido); nulo quando estão corretos.</summary>
    [ObservableProperty]
    public partial string? MarkerProblem { get; set; }

    /// <summary>Falha ao gravar ou aprovar, em português; nulo quando a última gravação deu certo.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool HasMissingMarkers => MissingMarkers.Count > 0;

    /// <summary>Aviso mostrado abaixo do campo quando faltam marcadores.</summary>
    public string MissingMarkersText => MissingMarkers.Count == 0
        ? string.Empty
        : $"Faltam na tradução os marcadores do original: {string.Join(", ", MissingMarkers)}";

    public bool IsApproved => Status == TranslationStatus.Approved;

    public string StatusText => Status switch
    {
        TranslationStatus.Untranslated => "Sem tradução",
        TranslationStatus.Machine => "Tradução da IA",
        TranslationStatus.Translated => "Traduzida",
        TranslationStatus.Reviewed => "Revisada",
        TranslationStatus.Approved => "Aprovada",
        _ => Status.ToString(),
    };

    /// <summary>Marcadores do original ausentes na tradução, na ordem do original. Texto em branco não acusa nada.</summary>
    public static IReadOnlyList<string> FindMissingMarkers(IReadOnlyList<string> sourceMarkers, IReadOnlyList<string> translationMarkers)
    {
        ArgumentNullException.ThrowIfNull(sourceMarkers);
        ArgumentNullException.ThrowIfNull(translationMarkers);

        var available = translationMarkers.GroupBy(m => m, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var marker in sourceMarkers)
        {
            if (available.TryGetValue(marker, out var count) && count > 0)
            {
                available[marker] = count - 1;
            }
            else
            {
                missing.Add(marker);
            }
        }

        return missing;
    }

    /// <summary>A caixa de texto da linha com o perfil de formatação, refeita a cada edição da tradução; nulo até <see cref="AttachPreview"/> ou sem perfil legível.</summary>
    [ObservableProperty]
    public partial TextboxPreviewViewModel? Preview { get; private set; }

    /// <summary>Liga a prévia da caixa de texto com a tradução que está no campo; sem perfil (nulo), tira a prévia. O mesmo perfil não refaz nada.</summary>
    public void AttachPreview(FormattingProfile? profile)
    {
        if (Preview is not null && ReferenceEquals(Preview.Profile, profile))
        {
            return;
        }

        Preview = profile is null ? null : new TextboxPreviewViewModel(unit, profile, Translation);
        OnPropertyChanged(nameof(FitText));
    }

    /// <summary>Grava o que estiver pendente. Devolve falso (e preenche <see cref="ErrorMessage"/>) quando a gravação falha; a edição continua pendente.</summary>
    public bool Flush()
    {
        timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return TrySave(() =>
        {
            if (translationDirty)
            {
                Saved = editor.SetTranslation(scriptKey, unit.Id, Translation);
                Status = Saved.Status;
                translationDirty = false;
            }

            if (notesDirty)
            {
                Saved = editor.SetNotes(scriptKey, unit.Id, Notes);
                notesDirty = false;
            }
        });
    }

    [RelayCommand]
    private void Approve() => ApproveCore();

    [RelayCommand]
    private void ApproveAndNext()
    {
        if (ApproveCore())
        {
            navigator.SelectNext();
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (Flush())
        {
            navigator.SelectNext();
        }
    }

    [RelayCommand]
    private void Previous()
    {
        if (Flush())
        {
            navigator.SelectPrevious();
        }
    }

    /// <summary>Grava o que estiver pendente e libera o temporizador. O dono do painel chama isto ao trocar de linha.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Flush();
        disposed = true;
        CancelSuggestion();
        timer?.Dispose();
        timer = null;
    }

    partial void OnTranslationChanged(string value)
    {
        if (loading)
        {
            return;
        }

        translationDirty = true;
        UpdateMarkers();
        Preview?.Update(value);
        OnPropertyChanged(nameof(FitText));
        ScheduleSave();
    }

    partial void OnNotesChanged(string value)
    {
        if (loading)
        {
            return;
        }

        notesDirty = true;
        ScheduleSave();
    }

    private bool ApproveCore()
    {
        if (!Flush())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Translation))
        {
            ErrorMessage = "A linha não tem tradução e não pode ser aprovada. Escreva a tradução antes de aprovar.";
            return false;
        }

        return TrySave(() =>
        {
            Saved = editor.Approve(scriptKey, unit.Id);
            Status = Saved.Status;
        });
    }

    private bool TrySave(Action save)
    {
        try
        {
            save();
            ErrorMessage = null;
            return true;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = $"Não foi possível gravar a linha: {e.Message} O texto continua na tela e será gravado na próxima tentativa.";
            return false;
        }
    }

    private void ScheduleSave()
    {
        if (disposed)
        {
            return;
        }

        if (timer is null)
        {
            timer = clock.CreateTimer(_ => postToUi(OnSaveTimer), null, SaveDelay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSaveTimer()
    {
        if (!disposed)
        {
            Flush();
        }
    }

    private void UpdateMarkers()
    {
        if (string.IsNullOrWhiteSpace(Translation))
        {
            MissingMarkers = [];
            MarkerProblem = null;
            SetMarkersMatch(false);
            return;
        }

        try
        {
            var found = ControlMarkers.Markers(Translation);
            MissingMarkers = FindMissingMarkers(sourceMarkers, found);
            MarkerProblem = null;
            SetMarkersMatch(MissingMarkers.Count == 0 && found.Count == sourceMarkers.Count);
        }
        catch (ArgumentException e)
        {
            MissingMarkers = [];
            MarkerProblem = e.Message;
            SetMarkersMatch(false);
        }
    }

    private void SetMarkersMatch(bool match)
    {
        MarkersMatch = match;
        OnPropertyChanged(nameof(MarkersMatch));
        OnPropertyChanged(nameof(MarkerStateText));
    }

    private static IReadOnlyList<TextSegment> SplitOrPlain(string source, out IReadOnlyList<string> markers)
    {
        try
        {
            var segments = ControlMarkers.Split(source);
            markers = [.. segments.Where(s => s.IsMarker).Select(s => s.Text)];
            return segments;
        }
        catch (ArgumentException)
        {
            // Original com barra ou chave fora da gramática: mostra o texto cru, sem chips e sem exigir marcadores.
            markers = [];
            return [new TextSegment(source, IsMarker: false)];
        }
    }

    private static Action<Action> CaptureContext()
    {
        var context = SynchronizationContext.Current;
        return context is null ? action => action() : action => context.Post(_ => action(), null);
    }
}
