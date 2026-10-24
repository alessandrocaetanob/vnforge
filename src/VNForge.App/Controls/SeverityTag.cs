using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using VNForge.App.Services;

namespace VNForge.App.Controls;

/// <summary>Gravidade do resultado do QA: erro bloqueia o build, aviso não, "ok" é nenhum problema.</summary>
public enum SeverityLevel
{
    Ok,
    Warning,
    Error,
}

/// <summary>O texto do <see cref="SeverityTag"/>: contagem no formato pt-BR (<c>1.204 avisos</c>) e plural correto.</summary>
public static class SeverityText
{
    public static string Format(SeverityLevel level, int? count) => level switch
    {
        SeverityLevel.Ok => "Sem problemas",
        SeverityLevel.Error => Word(count, "erro", "erros", "Erro"),
        _ => Word(count, "aviso", "avisos", "Aviso"),
    };

    private static string Word(int? count, string one, string many, string bare) => count switch
    {
        null => bare,
        1 => $"1 {one}",
        { } n => $"{ProjectLabels.Number(n)} {many}",
    };
}

/// <summary>
/// Resultado do QA para uma linha, um arquivo ou o projeto (docs/design/components/SeverityTag): ícone, contagem e palavra, nunca só a cor.
/// A aparência vem de <c>Themes/Controls/SeverityTag.axaml</c>; o nível vira a pseudo-classe <c>:error</c>, <c>:warning</c> ou <c>:ok</c>.
/// </summary>
public sealed class SeverityTag : TemplatedControl
{
    public static readonly StyledProperty<SeverityLevel> LevelProperty =
        AvaloniaProperty.Register<SeverityTag, SeverityLevel>(nameof(Level), SeverityLevel.Ok);

    public static readonly StyledProperty<int?> CountProperty =
        AvaloniaProperty.Register<SeverityTag, int?>(nameof(Count));

    public static readonly DirectProperty<SeverityTag, string> TextProperty =
        AvaloniaProperty.RegisterDirect<SeverityTag, string>(nameof(Text), o => o.Text);

    private string text = SeverityText.Format(SeverityLevel.Ok, null);

    public SeverityTag() => Apply();

    public SeverityLevel Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Quantas ocorrências; sem valor, o texto é só a palavra ("Erro", "Aviso"). Ignorada em <see cref="SeverityLevel.Ok"/>.</summary>
    public int? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>O texto que o controle mostra e que leitores de tela anunciam.</summary>
    public string Text
    {
        get => text;
        private set => SetAndRaise(TextProperty, ref text, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LevelProperty || change.Property == CountProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        Text = SeverityText.Format(Level, Count);
        AutomationProperties.SetName(this, Text);
        PseudoClasses.Set(":ok", Level == SeverityLevel.Ok);
        PseudoClasses.Set(":warning", Level == SeverityLevel.Warning);
        PseudoClasses.Set(":error", Level == SeverityLevel.Error);
    }
}
