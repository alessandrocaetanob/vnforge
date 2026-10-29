using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Services;

/// <summary>
/// Tamanho do texto e fonte do japonês. As escalas de <c>Themes/Typography.axaml</c> e as telas leem os tamanhos pelos tokens <c>Vnf.FontSize.*</c>,
/// <c>Vnf.LineHeight.*</c> e <c>Vnf.RowHeight</c>; <see cref="Build"/> devolve esses tokens já multiplicados, e a janela os põe em seus recursos.
/// Os valores de <c>Themes/Tokens.axaml</c> são os de 100%.
/// </summary>
public static class TextAppearance
{
    private const string EmbeddedJapanese = "avares://VNForge.App/Assets/Fonts#Noto Sans JP";

    private const double BaseRowHeight = 36;

    private const double BasePillHeight = 20;

    /// <summary>Nome do token, tamanho e altura de linha a 100%.</summary>
    public static IReadOnlyList<(string Name, double Size, double Line)> Scales { get; } =
    [
        ("Body", 14, 20),
        ("BodySm", 13, 18),
        ("Caption", 12, 16),
        ("Mono", 12, 18),
        ("MonoXs", 11, 14),
        ("Pill", 11, 14),
        ("TitleMd", 17, 25),
        ("JpBody", 15, 24),
        ("JpRow", 13, 18),
        ("BodyLoose", 14, 22),
        ("ReviewSource", 18, 27),
        ("ReviewTranslation", 20, 30),
    ];

    public static string FontSizeKey(string name) => $"Vnf.FontSize.{name}";

    public static string LineHeightKey(string name) => $"Vnf.LineHeight.{name}";

    /// <summary>Os tokens de tamanho e de altura de linha para <paramref name="percent"/>.</summary>
    public static ResourceDictionary Build(int percent)
    {
        var factor = percent / 100d;
        var resources = new ResourceDictionary();
        foreach (var (name, size, line) in Scales)
        {
            resources[FontSizeKey(name)] = Math.Round(size * factor, 1);
            resources[LineHeightKey(name)] = Math.Ceiling(line * factor);
        }

        resources["Vnf.RowHeight"] = Math.Round(BaseRowHeight * factor);
        resources["Vnf.PillHeight"] = Math.Round(BasePillHeight * factor);
        return resources;
    }

    /// <summary>A fonte escolhida primeiro e a Noto Sans JP embutida atrás, para o sistema sem a fonte não cair em outra sem kanji.</summary>
    public static FontFamily JapaneseFamily(JapaneseFontChoice font) => font switch
    {
        JapaneseFontChoice.YuGothic => new FontFamily($"Yu Gothic UI, {EmbeddedJapanese}"),
        JapaneseFontChoice.Meiryo => new FontFamily($"Meiryo UI, {EmbeddedJapanese}"),
        JapaneseFontChoice.MsGothic => new FontFamily($"MS Gothic, {EmbeddedJapanese}"),
        _ => new FontFamily(EmbeddedJapanese),
    };

    /// <summary>Texto do tamanho para o seletor, como <c>110%</c>.</summary>
    public static string PercentLabel(int percent) => string.Create(CultureInfo.InvariantCulture, $"{percent}%");
}
