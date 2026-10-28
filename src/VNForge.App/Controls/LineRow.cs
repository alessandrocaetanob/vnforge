using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VNForge.Core.Text;

namespace VNForge.App.Controls;

/// <summary>Indicador do QA na última coluna da linha da grade.</summary>
public enum LineIssue
{
    None,
    Warning,
    Error,
}

/// <summary>
/// O conteúdo de uma linha da grade de tradução (docs/design/components/LineRow): número, status, personagem, original, tradução e indicador do QA,
/// nas colunas <c>52 · 96 · 96 · 1* · 1* · 24</c>. Seleção, hover e foco vêm do <c>ListBoxItem</c> que a contém (<c>ListBox.lines</c>).
/// </summary>
public sealed class LineRow : TemplatedControl
{
    public const string NarrationLabel = "Narração";
    public const string NoTranslationLabel = "sem tradução";

    /// <summary>O que a coluna da tradução diz numa linha "Não traduzir": o build a copia como está.</summary>
    public const string KeepLabel = "fica como no original";

    public static readonly StyledProperty<int> NumberProperty = AvaloniaProperty.Register<LineRow, int>(nameof(Number));
    public static readonly StyledProperty<TranslationStatus> StatusProperty = AvaloniaProperty.Register<LineRow, TranslationStatus>(nameof(Status));
    public static readonly StyledProperty<string?> SpeakerProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(Speaker));
    public static readonly StyledProperty<bool> SpeakerChangedProperty = AvaloniaProperty.Register<LineRow, bool>(nameof(SpeakerChanged));
    public static readonly StyledProperty<string?> SpeakerTipProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(SpeakerTip));
    public static readonly StyledProperty<string?> SpeakerOriginalProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(SpeakerOriginal));
    public static readonly StyledProperty<string?> OriginalProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(Original));
    public static readonly StyledProperty<string?> TranslationProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(Translation));
    public static readonly StyledProperty<LineIssue> IssueProperty = AvaloniaProperty.Register<LineRow, LineIssue>(nameof(Issue));
    public static readonly StyledProperty<string?> IssueLabelProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(IssueLabel));
    public static readonly StyledProperty<string?> IssueTipProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(IssueTip));

    public static readonly DirectProperty<LineRow, string> NumberTextProperty =
        AvaloniaProperty.RegisterDirect<LineRow, string>(nameof(NumberText), o => o.NumberText);

    public static readonly DirectProperty<LineRow, string> SpeakerTextProperty =
        AvaloniaProperty.RegisterDirect<LineRow, string>(nameof(SpeakerText), o => o.SpeakerText);

    public static readonly DirectProperty<LineRow, string> TranslationTextProperty =
        AvaloniaProperty.RegisterDirect<LineRow, string>(nameof(TranslationText), o => o.TranslationText);

    private string numberText = "0000";
    private string speakerText = NarrationLabel;
    private string translationText = NoTranslationLabel;
    private SelectableTextBlock? speakerBlock;

    public LineRow() => Apply();

    public int Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public TranslationStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>Nome do personagem; vazio é narração.</summary>
    public string? Speaker
    {
        get => GetValue(SpeakerProperty);
        set => SetValue(SpeakerProperty, value);
    }

    /// <summary>A linha tem o falante trocado por quem traduz: o nome sai em destaque.</summary>
    public bool SpeakerChanged
    {
        get => GetValue(SpeakerChangedProperty);
        set => SetValue(SpeakerChangedProperty, value);
    }

    /// <summary>Dica do nome do falante trocado: o que o jogo diz nessa linha.</summary>
    public string? SpeakerTip
    {
        get => GetValue(SpeakerTipProperty);
        set => SetValue(SpeakerTipProperty, value);
    }

    /// <summary>O nome do personagem como está no texto extraído, quando <see cref="Speaker"/> mostra a tradução; é o que o menu do nome copia. Sem ele vale o próprio <see cref="Speaker"/>.</summary>
    public string? SpeakerOriginal
    {
        get => GetValue(SpeakerOriginalProperty);
        set => SetValue(SpeakerOriginalProperty, value);
    }

    public string? Original
    {
        get => GetValue(OriginalProperty);
        set => SetValue(OriginalProperty, value);
    }

    /// <summary>A tradução; vazia mostra "sem tradução" em <c>text-secondary</c>.</summary>
    public string? Translation
    {
        get => GetValue(TranslationProperty);
        set => SetValue(TranslationProperty, value);
    }

    public LineIssue Issue
    {
        get => GetValue(IssueProperty);
        set => SetValue(IssueProperty, value);
    }

    /// <summary>Texto do indicador do QA para leitor de tela ("1 aviso do QA").</summary>
    public string? IssueLabel
    {
        get => GetValue(IssueLabelProperty);
        set => SetValue(IssueLabelProperty, value);
    }

    /// <summary>Dica do ícone do QA: o nome da regra e a mensagem de cada ocorrência.</summary>
    public string? IssueTip
    {
        get => GetValue(IssueTipProperty);
        set => SetValue(IssueTipProperty, value);
    }

    /// <summary>O número da linha em mono, com zeros à esquerda (<c>0038</c>).</summary>
    public string NumberText
    {
        get => numberText;
        private set => SetAndRaise(NumberTextProperty, ref numberText, value);
    }

    public string SpeakerText
    {
        get => speakerText;
        private set => SetAndRaise(SpeakerTextProperty, ref speakerText, value);
    }

    public string TranslationText
    {
        get => translationText;
        private set => SetAndRaise(TranslationTextProperty, ref translationText, value);
    }

    /// <summary>
    /// O nome de quem fala é selecionável, com o menu do <see cref="SpeakerMenu"/> (copiar o nome original, a tradução ou a seleção). O <see cref="SelectableTextBlock"/> trata o clique para começar a seleção, e a lista
    /// deixaria de ouvi-lo: a linha se seleciona aqui.
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (speakerBlock is not null)
        {
            speakerBlock.RemoveHandler(PointerPressedEvent, OnSpeakerPressed);
        }

        speakerBlock = e.NameScope.Find<SelectableTextBlock>("PART_Speaker");
        if (speakerBlock is not null)
        {
            speakerBlock.AddHandler(PointerPressedEvent, OnSpeakerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            Apply();
        }
    }

    private void OnSpeakerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed
            && this.FindAncestorOfType<ListBoxItem>() is { IsSelected: false } item)
        {
            item.IsSelected = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NumberProperty || change.Property == SpeakerProperty || change.Property == SpeakerOriginalProperty || change.Property == TranslationProperty
            || change.Property == IssueProperty || change.Property == StatusProperty || change.Property == SpeakerChangedProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var narration = string.IsNullOrEmpty(Speaker);
        var untranslated = string.IsNullOrEmpty(Translation);
        NumberText = Number.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        var speaker = narration ? NarrationLabel : Speaker!;
        if (speaker != SpeakerText)
        {
            // A linha é reciclada pela lista: a seleção do nome anterior não vale para o novo.
            speakerBlock?.ClearSelection();
        }

        SpeakerText = speaker;
        if (speakerBlock is not null)
        {
            // A narração não tem menu; com personagem cadastrado a grade mostra a tradução e o menu copia o original (ou a tradução, à escolha).
            var original = narration ? null : SpeakerOriginal ?? Speaker;
            SpeakerMenu.SetOriginal(speakerBlock, original);
            SpeakerMenu.SetTranslation(speakerBlock, original is not null && original != Speaker ? Speaker : null);
        }

        TranslationText = !untranslated ? Translation! : Status == TranslationStatus.Keep ? KeepLabel : NoTranslationLabel;
        PseudoClasses.Set(":narration", narration);
        PseudoClasses.Set(":untranslated", untranslated);
        PseudoClasses.Set(":speaker-changed", SpeakerChanged);
        PseudoClasses.Set(":warning", Issue == LineIssue.Warning);
        PseudoClasses.Set(":error", Issue == LineIssue.Error);
        AutomationProperties.SetName(this, SpeakerChanged ? $"Linha {NumberText}, falante trocado" : $"Linha {NumberText}");
    }
}
