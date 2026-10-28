using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Um chip de "Reescrever com um toque": a direção, o rótulo, a dica e o comando que pede as duas versões.</summary>
public sealed record RewriteChip(RewriteDirection Direction, string Label, string Tip, IAsyncRelayCommand Command);

/// <summary>Uma das duas versões que a IA devolveu para a fala, já medida contra a caixa do jogo.</summary>
/// <param name="Text">A versão, com os marcadores do original.</param>
/// <param name="FitText">"Cabe · 2 de 3 linhas" ou "Passa 1 linha"; vazio sem perfil de formatação ou quando a medida não vale.</param>
/// <param name="Overflows">A versão passa do limite de linhas da caixa; falso quando cabe ou não há medida.</param>
/// <param name="UseCommand">Troca a tradução por esta versão.</param>
/// <param name="UseLabel">O nome do botão Usar para leitores de tela: "Usar a versão 1".</param>
public sealed record RewriteOption(string Text, string FitText, bool Overflows, IRelayCommand UseCommand, string UseLabel)
{
    public bool HasFit => FitText.Length > 0;

    public bool Fits => HasFit && !Overflows;
}

/// <summary>"Reescrever com um toque": chips que pedem à IA duas versões da tradução numa direção (mais curta, mais natural, mais fiel, mais formal, como o personagem falaria).</summary>
public sealed partial class LineDetailViewModel
{
    private CancellationTokenSource? rewriteCancellation;
    private IReadOnlyList<RewriteChip>? rewriteChips;
    private string rewriteBase = string.Empty;

    private const string RewriteStaleText = "A tradução mudou enquanto a IA reescrevia. Peça de novo para partir do texto de agora.";

    /// <summary>Quem reescreve: o mesmo sugestor do painel, quando ele sabe reescrever; sem ele os chips não aparecem.</summary>
    public ILineRewriter? Rewriter => Suggester as ILineRewriter;

    public bool HasRewriter => Rewriter is not null;

    /// <summary>Os cinco chips, na ordem do inspetor e do modo revisão.</summary>
    public IReadOnlyList<RewriteChip> RewriteChips => rewriteChips ??=
    [
        Chip(RewriteDirection.Shorter, "Mais curta", "Duas versões mais curtas, com o mesmo sentido, que cabem na caixa do jogo."),
        Chip(RewriteDirection.MoreNatural, "Mais natural", "Duas versões que soam mais naturais em português falado."),
        Chip(RewriteDirection.MoreFaithful, "Mais fiel", "Duas versões mais próximas do que o original diz."),
        Chip(RewriteDirection.MoreFormal, "Mais formal", "Duas versões em registro mais formal e respeitoso."),
        Chip(RewriteDirection.AsCharacter, "Como o personagem falaria", "Duas versões no jeito de falar do personagem (o \"Como fala\" dele no Glossário)."),
    ];

    /// <summary>A IA está reescrevendo a fala.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRewriteResult))]
    [NotifyCanExecuteChangedFor(nameof(CancelRewriteCommand))]
    public partial bool IsRewriting { get; private set; }

    /// <summary>As duas versões da última reescrita; vazia sem resultado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRewriteOptions), nameof(ShowRewriteResult))]
    public partial IReadOnlyList<RewriteOption> RewriteOptions { get; private set; } = [];

    /// <summary>O título do resultado: "Mais curta · 2 versões".</summary>
    [ObservableProperty]
    public partial string RewriteTitle { get; private set; } = string.Empty;

    /// <summary>Aviso sobre a origem das versões: de teste, sem chave de API; vazio com o Claude.</summary>
    [ObservableProperty]
    public partial string RewriteNote { get; private set; } = string.Empty;

    /// <summary>Falha ao pedir a reescrita, em português; nulo quando o último pedido deu certo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRewriteError))]
    public partial string? RewriteError { get; private set; }

    public bool HasRewriteOptions => RewriteOptions.Count > 0;

    public bool HasRewriteError => !string.IsNullOrEmpty(RewriteError);

    /// <summary>O cartão das versões aparece com versões prontas ou enquanto o pedido roda.</summary>
    public bool ShowRewriteResult => HasRewriteOptions || IsRewriting;

    private RewriteChip Chip(RewriteDirection direction, string label, string tip) =>
        new(direction, label, tip, new AsyncRelayCommand(() => RewriteAsync(direction), () => Rewriter is not null && !IsRewriting));

    private async Task RewriteAsync(RewriteDirection direction)
    {
        if (Rewriter is not { } rewriter || disposed)
        {
            return;
        }

        RewriteOptions = [];
        RewriteError = null;
        RewriteNote = string.Empty;

        // Grava antes de ler: a normalização da pontuação muda o campo ao gravar, e a conferência de "a tradução mudou" compara com o texto de depois dela.
        if (!Flush())
        {
            return;
        }

        var current = Translation;
        if (RewriteBlockedReason(direction, current) is { } blocked)
        {
            RewriteError = blocked;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        rewriteCancellation = cancellation;
        var label = RewriteChips.First(c => c.Direction == direction).Label;
        RewriteTitle = label;
        IsRewriting = true;
        try
        {
            var box = Preview?.Profile.Wrap;
            var result = await rewriter.RewriteAsync(RewriteBatch(), direction, current, box, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                ShowRewrite(result, label, current);
            }
        }
        catch (Exception e)
        {
            // O provedor pode lançar de tudo (rede, SDK, recusa); um pedido cancelado por Dispose ou pelo usuário não mostra erro.
            if (!cancellation.IsCancellationRequested)
            {
                var reason = e is LineSuggestionException ? e.Message : ClaudeErrorMessages.Describe(e);
                RewriteError = $"Não foi possível reescrever: {reason} A tradução da linha não mudou.";
            }
        }
        finally
        {
            if (ReferenceEquals(rewriteCancellation, cancellation))
            {
                rewriteCancellation = null;
            }

            IsRewriting = false;
        }
    }

    /// <summary>Por que o pedido nem sai: sem texto para mudar, ou "Como o personagem falaria" sem o "Como fala" ou na narração. Nulo quando pode seguir.</summary>
    private string? RewriteBlockedReason(RewriteDirection direction, string current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return "Escreva ou peça uma tradução antes de reescrever: não há texto para mudar.";
        }

        if (direction != RewriteDirection.AsCharacter || HasVoice)
        {
            return null;
        }

        return Speaker is null
            ? "A narração não tem personagem. Escolha outra direção."
            : $"{SpeakerName} ainda não tem o Como fala. Defina no Glossário e peça de novo.";
    }

    /// <summary>Mostra as versões, a menos que a tradução tenha mudado enquanto a IA respondia: aí elas partem de um texto que já não existe.</summary>
    private void ShowRewrite(LineRewrite result, string label, string current)
    {
        if (Translation != current)
        {
            RewriteTitle = string.Empty;
            RewriteError = RewriteStaleText;
            return;
        }

        rewriteBase = current;
        RewriteOptions = [.. result.Options.Select((text, i) => MakeOption(text, i + 1))];
        RewriteTitle = result.Options.Count == 1 ? $"{label} · 1 versão" : $"{label} · {result.Options.Count} versões";
        RewriteNote = result.FromFakeProvider ? "Versões de teste, sem chave de API. Configure a chave em Ajustes para usar o Claude." : string.Empty;
    }

    /// <summary>
    /// A linha com o contexto do pedido, como o lote de tradução monta: o falante de agora (depois de "Trocar falante") com o "Como fala", o glossário da fala,
    /// o estilo e o guia de estilo do projeto e as falas vizinhas da cena.
    /// </summary>
    private TranslationBatch RewriteBatch()
    {
        var (style, guide) = ProjectStyle();
        var scene = Scene.Select(l => (l.Following, Unit: new TextUnit($"cena:{l.Who}", TextUnitKind.Dialogue, l.Who, l.Original, l.Translation, TranslationStatus.Untranslated))).ToList();
        return new TranslationBatch([Saved with { Speaker = Speaker }])
        {
            Characters = SpeakerInfo() is { } speaker ? [speaker] : [],
            Glossary = [.. (Context?.Glossary ?? []).Select(g => new GlossaryEntry(g.Source, g.Target, g.Notes))],
            Style = style,
            StyleGuide = guide,
            PreviousLines = [.. scene.Where(l => !l.Following).Select(l => l.Unit)],
            NextLines = [.. scene.Where(l => l.Following).Select(l => l.Unit)],
        };
    }

    /// <summary>O estilo e o guia de estilo do projeto; o padrão sem projeto ou quando os arquivos não abrem.</summary>
    private (StyleConfig Style, string Guide) ProjectStyle()
    {
        if (ProjectRoot is not { } root)
        {
            return (StyleConfig.Default, string.Empty);
        }

        try
        {
            var style = ProjectFolder.Open(root).LoadConfig().Style;
            return (style, TranslationBatchBuilder.ReadStyleGuide(Path.Combine(root, TranslationContextSummary.StyleGuideFileName)));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return (StyleConfig.Default, string.Empty);
        }
    }

    /// <summary>O falante no formato que o provedor lê: o nome, o gênero e o "Como fala"; nulo na narração, em "？？？" (que serve a várias pessoas) e em quem não tem cadastro nem tom.</summary>
    private CharacterInfo? SpeakerInfo()
    {
        if (Speaker is not { } speaker || SpeakerNames.IsSignsOnly(speaker))
        {
            return null;
        }

        var entry = Characters.FirstOrDefault(c => string.Equals(c.Source, speaker, StringComparison.Ordinal));
        var tone = Voice ?? entry?.Tone;
        if (entry is null && tone is null)
        {
            return null;
        }

        var gender = entry is null || entry.Gender == CharacterGender.Unknown ? null : entry.Gender.ToString().ToLowerInvariant();
        return new CharacterInfo(speaker, entry?.Target ?? SpeakerTarget ?? speaker, tone, gender);
    }

    private RewriteOption MakeOption(string text, int number)
    {
        var (fit, overflows) = MeasureRewrite(text);
        return new RewriteOption(text, fit, overflows, new RelayCommand(() => UseRewrite(text)), $"Usar a versão {number}");
    }

    /// <summary>A versão contra a caixa do jogo, pela mesma medida do "Cabe · 2 de 3 linhas" do inspetor; vazia sem perfil ou quando o perfil não vale para a linha.</summary>
    private (string FitText, bool Overflows) MeasureRewrite(string text)
    {
        if (Preview is not { } preview)
        {
            return (string.Empty, false);
        }

        var measured = new TextboxPreviewViewModel(Saved, preview.Profile, text, preview.Marks, preview.Naming);
        if (measured.Layout is not { } layout || measured.Notice is not null)
        {
            return (string.Empty, false);
        }

        var over = layout.Boxes.Sum(b => b.Lines.Count(l => l.Overflow));
        if (over > 0)
        {
            return ($"Passa {LinesOf(over)}", true);
        }

        if (!preview.Profile.Wrap.Enabled)
        {
            return (string.Empty, false);
        }

        var lines = layout.Boxes.Select(b => b.Lines.Count).DefaultIfEmpty().Max();
        return (layout.MaxLines is { } max ? $"Cabe · {lines} de {LinesOf(max)}" : $"Cabe · {LinesOf(lines)}", false);
    }

    private void UseRewrite(string text)
    {
        // As versões partem da tradução de quando foram pedidas: se a pessoa a editou depois, usar uma delas apagaria a edição.
        if (Translation != rewriteBase)
        {
            ClearRewrite();
            RewriteError = RewriteStaleText;
            return;
        }

        Translation = text;
        ClearRewrite();
    }

    [RelayCommand]
    private void DiscardRewrite() => ClearRewrite();

    private void ClearRewrite()
    {
        RewriteOptions = [];
        RewriteTitle = string.Empty;
        RewriteNote = string.Empty;
        RewriteError = null;
    }

    [RelayCommand(CanExecute = nameof(IsRewriting))]
    private void CancelRewrite() => rewriteCancellation?.Cancel();

    partial void OnIsRewritingChanged(bool value)
    {
        foreach (var chip in RewriteChips)
        {
            chip.Command.NotifyCanExecuteChanged();
        }
    }
}
