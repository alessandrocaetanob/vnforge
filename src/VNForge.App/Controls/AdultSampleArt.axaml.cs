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

    // O raio de 16 px do quadro e da issue; a arte cresce além da caixa (16 px de cada lado numa caixa de 96 por 128 pedem um terço) para o borrão não deixar borda transparente, e a caixa corta o excesso.
    private void Apply()
    {
        if (this.FindControl<Viewbox>("Art") is not { } art)
        {
            return;
        }

        art.Effect = Blurred ? new BlurEffect { Radius = 16 } : null;
        art.RenderTransform = Blurred ? new ScaleTransform(1.4, 1.4) : null;
    }
}
