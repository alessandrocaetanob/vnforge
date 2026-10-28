using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using VNForge.Core.Glossary;

namespace VNForge.App.Controls;

/// <summary>
/// Uma cor da paleta de personagens (#467). Cada cor tem uma versão para o tema claro e outra para o escuro, as duas com contraste de pelo menos 4,5:1
/// contra os fundos do app (cartão, base, seleção) e, a escura, também contra a placa da caixa do jogo.
/// </summary>
public sealed class CharacterColor
{
    internal CharacterColor(string key, string label, Color light, Color dark)
    {
        Key = key;
        Label = label;
        Light = light;
        Dark = dark;
        LightBrush = new ImmutableSolidColorBrush(light);
        DarkBrush = new ImmutableSolidColorBrush(dark);
    }

    /// <summary>O id que vai para <c>characters.json</c> (<see cref="CharacterEntry.Color"/>).</summary>
    public string Key { get; }

    /// <summary>O nome da cor, em português, para o seletor do Glossário.</summary>
    public string Label { get; }

    public Color Light { get; }

    public Color Dark { get; }

    public IBrush LightBrush { get; }

    public IBrush DarkBrush { get; }

    /// <summary>A tinta no tema dado; <see langword="null"/> ou o tema padrão (que o Avalonia resolve para o do sistema) valem pelo claro.</summary>
    public IBrush BrushFor(ThemeVariant? theme) => theme == ThemeVariant.Dark ? DarkBrush : LightBrush;

    public override string ToString() => Label;
}

/// <summary>A paleta fixa de cores de personagem e como o app escolhe uma quando o <c>characters.json</c> não traz a cor.</summary>
public static class CharacterPalette
{
    public static IReadOnlyList<CharacterColor> All { get; } =
    [
        New("blue", "Azul", "#1F5FBF", "#A6C8FF"),
        New("red", "Vermelho", "#B3322A", "#FFB0A6"),
        New("green", "Verde", "#1B7237", "#93E5A6"),
        New("gold", "Dourado", "#8A5F00", "#F2D06B"),
        New("violet", "Violeta", "#6B3FB3", "#D3BDFF"),
        New("rose", "Rosa", "#B02A6E", "#FFB3D6"),
        New("teal", "Turquesa", "#0B7285", "#7FDCEB"),
        New("orange", "Laranja", "#A64400", "#FFC08A"),
    ];

    /// <summary>A cor de id <paramref name="key"/>; nula quando não há (id vazio ou de uma versão futura do app).</summary>
    public static CharacterColor? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(c => string.Equals(c.Key, key.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// A cor do personagem: a que o arquivo traz ou, sem ela, uma da paleta tirada do nome original. A escolha só depende do nome, então não muda
    /// de uma abertura do projeto para a outra nem com a ordem da lista.
    /// </summary>
    public static CharacterColor Resolve(string? key, string source) => Find(key) ?? Automatic(source);

    /// <summary>A cor de um personagem cadastrado; nula quando ele não está no Glossário (narração e falantes sem cadastro não têm cor).</summary>
    public static CharacterColor? For(CharacterEntry? character) =>
        character is null ? null : Resolve(character.Color, character.Source);

    private static CharacterColor Automatic(string source)
    {
        var hash = 2166136261u;
        foreach (var c in (source ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC))
        {
            hash = (hash ^ c) * 16777619u;
        }

        // O FNV-1a sozinho deixa nomes que só diferem num bit alto da última letra (学生A e 学生I) na mesma cor do módulo: o finalizador do Murmur3 espalha os bits.
        hash ^= hash >> 16;
        hash *= 0x85EBCA6B;
        hash ^= hash >> 13;
        hash *= 0xC2B2AE35;
        hash ^= hash >> 16;
        return All[(int)(hash % (uint)All.Count)];
    }

    private static CharacterColor New(string key, string label, string light, string dark) =>
        new(key, label, Color.Parse(light), Color.Parse(dark));
}

/// <summary>
/// Pinta um controle com a cor de um personagem no tema atual e a refaz quando o tema muda: <c>Foreground</c> num <see cref="TextBlock"/>, <c>Background</c>
/// num <see cref="Border"/> e <c>Fill</c> numa <see cref="Shape"/>. Sem cor, devolve ao controle a tinta que o estilo dele dá.
/// </summary>
public static class SpeakerColorTint
{
    public static readonly AttachedProperty<CharacterColor?> ColorProperty =
        AvaloniaProperty.RegisterAttached<Control, CharacterColor?>("Color", typeof(SpeakerColorTint));

    static SpeakerColorTint() => ColorProperty.Changed.AddClassHandler<Control>((control, _) => Attach(control));

    public static CharacterColor? GetColor(Control control) => control.GetValue(ColorProperty);

    public static void SetColor(Control control, CharacterColor? value) => control.SetValue(ColorProperty, value);

    private static void Attach(Control control)
    {
        control.ActualThemeVariantChanged -= OnThemeChanged;
        if (GetColor(control) is not null)
        {
            control.ActualThemeVariantChanged += OnThemeChanged;
        }

        Apply(control);
    }

    private static void OnThemeChanged(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            Apply(control);
        }
    }

    private static void Apply(Control control)
    {
        var brush = GetColor(control)?.BrushFor(control.ActualThemeVariant);
        switch (control)
        {
            case TextBlock text:
                Set(text, TextBlock.ForegroundProperty, brush);
                break;
            case Border border:
                Set(border, Border.BackgroundProperty, brush);
                break;
            case Shape shape:
                Set(shape, Shape.FillProperty, brush);
                break;
        }
    }

    private static void Set(AvaloniaObject target, AvaloniaProperty<IBrush?> property, IBrush? brush)
    {
        if (brush is null)
        {
            target.ClearValue(property);
        }
        else
        {
            target.SetValue(property, brush);
        }
    }
}
