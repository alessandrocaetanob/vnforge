using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Sugestão da IA para a linha atual, mostrada como diferença em relação à tradução existente (docs/design/components/AiSuggestion).
/// Inserções em <c>status-approved-*</c>, remoções riscadas em <c>text-secondary</c>. A sugestão nunca substitui o texto sozinha:
/// "Usar sugestão" dispara <see cref="UseCommand"/> e o editor decide. Enquanto a chamada roda, o corpo mostra uma barra indeterminada e o botão vira "Cancelar".
/// </summary>
public sealed class AiSuggestion : TemplatedControl
{
    public static readonly StyledProperty<string?> ModelProperty = AvaloniaProperty.Register<AiSuggestion, string?>(nameof(Model));
    public static readonly StyledProperty<string?> DetailsProperty = AvaloniaProperty.Register<AiSuggestion, string?>(nameof(Details));
    public static readonly StyledProperty<string?> CurrentProperty = AvaloniaProperty.Register<AiSuggestion, string?>(nameof(Current));
    public static readonly StyledProperty<string?> SuggestionProperty = AvaloniaProperty.Register<AiSuggestion, string?>(nameof(Suggestion));
    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<AiSuggestion, bool>(nameof(IsBusy));
    public static readonly StyledProperty<ICommand?> UseCommandProperty = AvaloniaProperty.Register<AiSuggestion, ICommand?>(nameof(UseCommand));
    public static readonly StyledProperty<ICommand?> CancelCommandProperty = AvaloniaProperty.Register<AiSuggestion, ICommand?>(nameof(CancelCommand));

    public static readonly DirectProperty<AiSuggestion, IReadOnlyList<DiffSegment>> SegmentsProperty =
        AvaloniaProperty.RegisterDirect<AiSuggestion, IReadOnlyList<DiffSegment>>(nameof(Segments), o => o.Segments);

    private IReadOnlyList<DiffSegment> segments = [];
    private SelectableTextBlock? body;

    /// <summary>O modelo, em <c>status-ai-fg</c> e 600 ("Claude Opus 5.5").</summary>
    public string? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>O resto do cabeçalho, em <c>text-secondary</c> ("· esforço médio · há 2 min").</summary>
    public string? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    /// <summary>A tradução que está no editor; vazia, a sugestão aparece sem marcação.</summary>
    public string? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    public string? Suggestion
    {
        get => GetValue(SuggestionProperty);
        set => SetValue(SuggestionProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public ICommand? UseCommand
    {
        get => GetValue(UseCommandProperty);
        set => SetValue(UseCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    /// <summary>A diferença por palavra que o corpo desenha.</summary>
    public IReadOnlyList<DiffSegment> Segments
    {
        get => segments;
        private set => SetAndRaise(SegmentsProperty, ref segments, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        body = e.NameScope.Find<SelectableTextBlock>("PART_Body");
        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentProperty || change.Property == SuggestionProperty)
        {
            Rebuild();
        }
        else if (change.Property == IsBusyProperty)
        {
            PseudoClasses.Set(":busy", IsBusy);
        }
    }

    private void Rebuild()
    {
        Segments = WordDiff.Compute(Current, Suggestion);
        if (body is null)
        {
            return;
        }

        body.Inlines ??= [];
        body.Inlines.Clear();
        for (var i = 0; i < Segments.Count; i++)
        {
            var segment = Segments[i];
            if (i > 0)
            {
                body.Inlines.Add(new Run(" "));
            }

            var run = new Run(segment.Text);
            if (segment.Kind == DiffKind.Insert)
            {
                run.Classes.Add("ins");
            }
            else if (segment.Kind == DiffKind.Delete)
            {
                run.Classes.Add("del");
            }

            body.Inlines.Add(run);
        }
    }
}
