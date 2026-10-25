using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Grupo de cenas por capítulo na lista do Editor (v3.css, <c>.grp</c>): um <see cref="Expander"/> com o cabeçalho próprio, o nome do capítulo em caixa normal
/// e, à direita, a contagem. Os grupos vêm do Core (capítulos propostos pelas pastas dos scripts).
/// </summary>
public sealed class ScriptGroup : Expander
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ScriptGroup, string?>(nameof(Title));
    public static readonly StyledProperty<string?> CountTextProperty = AvaloniaProperty.Register<ScriptGroup, string?>(nameof(CountText));

    public ScriptGroup() => IsExpanded = true;

    protected override Type StyleKeyOverride => typeof(ScriptGroup);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>O número à direita do cabeçalho ("14").</summary>
    public string? CountText
    {
        get => GetValue(CountTextProperty);
        set => SetValue(CountTextProperty, value);
    }
}