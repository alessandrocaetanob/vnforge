using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace VNForge.App.Controls;

/// <summary>
/// O palco do modo revisão: um retângulo 16:9 que imita a tela do jogo, com fundo escuro em degradê, vinheta, o nome da cena num cartão de capítulo no
/// canto e, na base, o <c>Content</c> (a caixa do jogo; com altura fixa, centrada no palco como no quadro). Cresce com o espaço recebido sem passar da largura nem da altura. Fica escuro nos dois temas,
/// porque é a tela do jogo e não a do app (tokens <c>Vnf.Stage*</c>).
/// </summary>
public sealed class ReviewStage : ContentControl
{
    /// <summary>Largura sobre altura do palco.</summary>
    public const double Aspect = 16d / 9d;

    /// <summary>A largura do palco quando nenhum dos eixos tem limite.</summary>
    public const double UnboundedWidth = 640;

    public static readonly StyledProperty<string?> SceneNameProperty = AvaloniaProperty.Register<ReviewStage, string?>(nameof(SceneName));

    public static readonly StyledProperty<double> FixedHeightProperty = AvaloniaProperty.Register<ReviewStage, double>(nameof(FixedHeight), double.NaN);

    /// <summary>
    /// Uma altura fixa para o palco, que então ocupa a largura toda que recebe, sem a proporção 16:9: é o palco da fila por risco, de 270 px como no quadro, que
    /// deixa o resto da coluna para a barra e a tabela. <see cref="double.NaN"/> mantém o 16:9 que cresce com o espaço.
    /// </summary>
    public double FixedHeight
    {
        get => GetValue(FixedHeightProperty);
        set => SetValue(FixedHeightProperty, value);
    }

    /// <summary>O nome da cena, no cartão de capítulo do canto superior esquerdo.</summary>
    public string? SceneName
    {
        get => GetValue(SceneNameProperty);
        set => SetValue(SceneNameProperty, value);
    }

    /// <summary>A altura do palco quando a passada de editor tem uma sugestão para a linha, como no quadro (#532).</summary>
    public const double SuggestionHeight = 360;

    static ReviewStage()
    {
        AffectsMeasure<ReviewStage>(FixedHeightProperty);
        FixedHeightProperty.Changed.AddClassHandler<ReviewStage>((stage, e) => stage.PseudoClasses.Set(":fixed", e.NewValue is double height && !double.IsNaN(height)));
    }

    /// <summary>
    /// A altura do palco: a da fila (<paramref name="queueHeight"/>, que é <see cref="double.NaN"/> fora da fila por risco) ou, com uma sugestão do editor na linha e sem
    /// altura própria da fila, <see cref="SuggestionHeight"/>. O palco só deixa o 16:9 para o aviso e o cartão da sugestão caberem como no quadro.
    /// </summary>
    public static double HeightFor(double queueHeight, bool hasSuggestion) => double.IsNaN(queueHeight) && hasSuggestion ? SuggestionHeight : queueHeight;

    /// <summary>Liga <see cref="HeightFor"/> ao <see cref="FixedHeight"/> por um <c>MultiBinding</c> de (altura da fila, há sugestão).</summary>
    public static IMultiValueConverter HeightConverter { get; } =
        new FuncMultiValueConverter<object?, double>(values => values.ToArray() is [double queue, bool has] ? HeightFor(queue, has) : double.NaN);

    /// <summary>O maior 16:9 que cabe em <paramref name="available"/>; sem limite em um dos eixos, vale o outro.</summary>
    public static Size Fit(Size available)
    {
        var width = double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width);
        var height = double.IsInfinity(available.Height) ? double.PositiveInfinity : Math.Max(0, available.Height);
        if (double.IsInfinity(width) && double.IsInfinity(height))
        {
            return new Size(UnboundedWidth, UnboundedWidth / Aspect);
        }

        var fitted = Math.Min(width, height * Aspect);
        return new Size(fitted, fitted / Aspect);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = double.IsNaN(FixedHeight)
            ? Fit(availableSize)
            : new Size(double.IsInfinity(availableSize.Width) ? UnboundedWidth : availableSize.Width, FixedHeight);
        base.MeasureOverride(size);
        return size;
    }
}
