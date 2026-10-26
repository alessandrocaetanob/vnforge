using Avalonia;
using Avalonia.Controls;
using VNForge.App.Services;

namespace VNForge.App.Controls;

/// <summary>
/// "Detalhes técnicos" (v3.css, <c>.tech</c>): um <see cref="Expander"/> fechado por padrão onde ficam o que a pessoa que traduz não precisa ver
/// (motor, pasta, acentos, nome do modelo, registro completo). O título vai em <c>Header</c> e o conteúdo é livre.
/// Com <see cref="Key"/> e uma <see cref="TechnicalDetailsMemory"/> herdada da janela (<see cref="MemoryProperty"/>), o estado aberto ou fechado
/// volta quando a página é recriada. Sem uma das duas, o bloco se comporta como um <see cref="Expander"/> comum.
/// </summary>
public sealed class TechnicalDetails : Expander
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<TechnicalDetails, string?>(nameof(Key));

    /// <summary>Onde o estado fica guardado; definida na janela, vale para tudo que está dentro dela.</summary>
    public static readonly AttachedProperty<TechnicalDetailsMemory?> MemoryProperty =
        AvaloniaProperty.RegisterAttached<TechnicalDetails, Control, TechnicalDetailsMemory?>("Memory", inherits: true);

    private bool remembering;

    public TechnicalDetails() => Header = "Detalhes técnicos";

    /// <summary>A página a que este bloco pertence (<c>build</c>, <c>conference</c>, <c>start</c>); sem chave, o bloco não lembra.</summary>
    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TechnicalDetails);

    public static TechnicalDetailsMemory? GetMemory(Control control) => control.GetValue(MemoryProperty);

    public static void SetMemory(Control control, TechnicalDetailsMemory? value) => control.SetValue(MemoryProperty, value);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var memory = GetMemory(this);
        if (Key is { } key && memory?.Get(key) is { } open)
        {
            IsExpanded = open;
        }

        remembering = Key is not null && memory is not null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        remembering = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (remembering && change.Property == IsExpandedProperty && Key is { } key)
        {
            GetMemory(this)?.Set(key, IsExpanded);
        }
    }
}
