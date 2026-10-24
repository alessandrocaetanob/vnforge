using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.Controls;

/// <summary>
/// Selo de status de uma linha (docs/design/components/StatusBadge): sempre com o texto do status, nunca só a cor.
/// A aparência vem de <c>Themes/Controls/StatusBadge.axaml</c>.
/// </summary>
public sealed class StatusBadge : TemplatedControl
{
    public static readonly StyledProperty<TranslationStatus> StatusProperty =
        AvaloniaProperty.Register<StatusBadge, TranslationStatus>(nameof(Status));

    public static readonly DirectProperty<StatusBadge, string> LabelProperty =
        AvaloniaProperty.RegisterDirect<StatusBadge, string>(nameof(Label), o => o.Label);

    private string label = ProjectLabels.Status(TranslationStatus.Untranslated);

    public StatusBadge() => Apply(Status);

    public TranslationStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>O texto do status em português, o mesmo que leitores de tela anunciam.</summary>
    public string Label
    {
        get => label;
        private set => SetAndRaise(LabelProperty, ref label, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
        {
            Apply(change.GetNewValue<TranslationStatus>());
        }
    }

    private void Apply(TranslationStatus status)
    {
        Label = ProjectLabels.Status(status);
        AutomationProperties.SetName(this, Label);
        PseudoClasses.Set(":pending", status == TranslationStatus.Untranslated);
    }
}
