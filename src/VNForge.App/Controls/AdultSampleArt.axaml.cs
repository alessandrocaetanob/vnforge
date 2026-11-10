using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>A arte de exemplo da capa (lua, colina e gato) da aba Privacidade; <see cref="Blurred"/> a mostra borrada, como o modo Desfocar.</summary>
public sealed partial class AdultSampleArt : UserControl
{
    public static readonly StyledProperty<bool> BlurredProperty = AvaloniaProperty.Register<AdultSampleArt, bool>(nameof(Blurred));

    static AdultSampleArt() => BlurredProperty.Changed.AddClassHandler<AdultSampleArt>((art, _) => art.Apply());

    public AdultSampleArt() => InitializeComponent();

    public bool Blurred
    {
        get => GetValue(BlurredProperty);
        set => SetValue(BlurredProperty, value);
    }

    // A arte cresce um quarto além da caixa para o borrão não deixar borda transparente; a caixa corta o excesso.
    private void Apply()
    {
        if (this.FindControl<Viewbox>("Art") is not { } art)
        {
            return;
        }

        art.Effect = Blurred ? new BlurEffect { Radius = 8 } : null;
        art.RenderTransform = Blurred ? new ScaleTransform(1.25, 1.25) : null;
    }
}
