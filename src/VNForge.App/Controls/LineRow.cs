using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    public static readonly StyledProperty<int> NumberProperty = AvaloniaProperty.Register<LineRow, int>(nameof(Number));
    public static readonly StyledProperty<TranslationStatus> StatusProperty = AvaloniaProperty.Register<LineRow, TranslationStatus>(nameof(Status));
    public static readonly StyledProperty<string?> SpeakerProperty = AvaloniaProperty.Register<LineRow, string?>(nameof(Speaker));
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NumberProperty || change.Property == SpeakerProperty || change.Property == TranslationProperty
            || change.Property == IssueProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var narration = string.IsNullOrEmpty(Speaker);
        var untranslated = string.IsNullOrEmpty(Translation);
        NumberText = Number.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        SpeakerText = narration ? NarrationLabel : Speaker!;
        TranslationText = untranslated ? NoTranslationLabel : Translation!;
        PseudoClasses.Set(":narration", narration);
        PseudoClasses.Set(":untranslated", untranslated);
        PseudoClasses.Set(":warning", Issue == LineIssue.Warning);
        PseudoClasses.Set(":error", Issue == LineIssue.Error);
        AutomationProperties.SetName(this, $"Linha {NumberText}");
    }
}
