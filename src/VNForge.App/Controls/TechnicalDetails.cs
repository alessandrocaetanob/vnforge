using Avalonia;
using Avalonia.Controls;
using VNForge.App.Services;

namespace VNForge.App.Controls;

/// <summary>
/// "Detalhes técnicos" (v3.css, <c>.tech</c>): um <see cref="Expander"/> fechado por padrão onde ficam o que a pessoa que traduz não precisa ver
/// (motor, pasta, acentos, nome do modelo, registro completo). O título vai em <c>Header</c> e o conteúdo é livre.
/// Com <see cref="Key"/>, o estado aberto ou fechado fica em <see cref="TechnicalDetailsMemory"/> e volta quando a página é recriada.
/// </summary>
public sealed class TechnicalDetails : Expander
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<TechnicalDetails, string?>(nameof(Key));

    private bool remembering;

    public TechnicalDetails() => Header = "Detalhes técnicos";

    /// <summary>A página a que este bloco pertence (<c>build</c>, <c>conference</c>, <c>start</c>); sem chave, o bloco não lembra.</summary>
    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TechnicalDetails);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Key is { } key && TechnicalDetailsMemory.Current.Get(key) is { } open)
        {
            IsExpanded = open;
        }

        remembering = Key is not null;
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
            TechnicalDetailsMemory.Current.Set(key, IsExpanded);
        }
    }
}
