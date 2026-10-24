using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace VNForge.App.Controls;

public enum PipelineStepState
{
    Pending,
    Active,
    Done,
}

/// <summary>Uma etapa do pipeline: número, nome e o que falta nela (só em Extrair o subtítulo diz o que já foi feito).</summary>
public sealed record PipelineStep(
    int Number,
    string Title,
    string Subtitle,
    PipelineStepState State,
    ICommand? Command = null,
    object? CommandParameter = null);

/// <summary>Uma etapa do <see cref="PipelineStepper"/>: botão com a pseudo-classe <c>:done</c> ou <c>:active</c>.</summary>
public sealed class PipelineStepItem : Button
{
    public static readonly StyledProperty<int> NumberProperty = AvaloniaProperty.Register<PipelineStepItem, int>(nameof(Number));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PipelineStepItem, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<PipelineStepItem, string?>(nameof(Subtitle));
    public static readonly StyledProperty<PipelineStepState> StateProperty =
        AvaloniaProperty.Register<PipelineStepItem, PipelineStepState>(nameof(State));

    public PipelineStepItem() => Apply();

    public int Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public PipelineStepState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    internal void Apply(PipelineStep step)
    {
        Number = step.Number;
        Title = step.Title;
        Subtitle = step.Subtitle;
        State = step.State;
        Command = step.Command;
        CommandParameter = step.CommandParameter;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty || change.Property == TitleProperty || change.Property == SubtitleProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        PseudoClasses.Set(":done", State == PipelineStepState.Done);
        PseudoClasses.Set(":active", State == PipelineStepState.Active);
        var state = State switch
        {
            PipelineStepState.Done => "concluída",
            PipelineStepState.Active => "atual",
            _ => "pendente",
        };
        AutomationProperties.SetName(this, $"{Title}, {state}. {Subtitle}");
    }
}

/// <summary>
/// As cinco etapas do pipeline como botões contíguos (docs/design/components/PipelineStepper). Os itens são <see cref="PipelineStep"/>;
/// a atual é a primeira não concluída, que <see cref="Resolve"/> calcula.
/// </summary>
public sealed class PipelineStepper : ItemsControl
{
    /// <summary>Marca as etapas concluídas como <see cref="PipelineStepState.Done"/>, a primeira pendente como atual e o resto como pendente.</summary>
    public static IReadOnlyList<PipelineStepState> Resolve(IReadOnlyList<bool> complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        var states = new PipelineStepState[complete.Count];
        var activeFound = false;
        for (var i = 0; i < complete.Count; i++)
        {
            if (complete[i])
            {
                states[i] = PipelineStepState.Done;
            }
            else if (!activeFound)
            {
                states[i] = PipelineStepState.Active;
                activeFound = true;
            }
        }

        return states;
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not PipelineStepItem;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new PipelineStepItem();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is PipelineStepItem step && item is PipelineStep data)
        {
            step.Apply(data);
        }
    }
}
