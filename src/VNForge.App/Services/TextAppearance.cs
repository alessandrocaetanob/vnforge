using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Services;

/// <summary>
/// Tamanho do texto e fonte do japonês. As escalas de <c>Themes/Typography.axaml</c> e as telas leem os tamanhos pelos tokens <c>Vnf.FontSize.*</c>,
/// <c>Vnf.LineHeight.*</c> e <c>Vnf.RowHeight</c>; <see cref="Build"/> devolve esses tokens já multiplicados, e a janela os põe em seus recursos.
/// Os valores de <c>Themes/TextScale.axaml</c> (escrito à mão) e a linha da grade de <c>Themes/Tokens.axaml</c> são os de 100%.
/// </summary>
public static class TextAppearance
{
    private const string EmbeddedJapanese = "avares://VNForge.App/Assets/Fonts#Noto Sans JP";

    private const double BaseRowHeight = 48;

    private const double BasePillHeight = 20;

    private const double BaseMarkerHeight = 22;

    /// <summary>A linha de um texto que leva selo de marcador: o selo (<see cref="BaseMarkerHeight"/>) mais 4 de folga, para o selo não encostar nas linhas vizinhas.</summary>
    private const double BaseMarkerLineHeight = 26;

    /// <summary>
    /// Nome do token, tamanho e altura de linha a 100%. São os do tokens.json (<c>TypographyTokensTests</c> confere), menos a altura de linha de
    /// <c>TitleLg</c> (35, não 32), <c>TitleMd</c> (25, não 24), <c>TitleXl</c> (50, não 40) e <c>TitleSm</c> (22, não 20): a fonte de exibição corta as descendentes abaixo dos 1,45 por corpo.
    /// </summary>
    public static IReadOnlyList<(string Name, double Size, double Line)> Scales { get; } =
    [
        ("Body", 14, 20),
        ("BodySm", 13, 18),
        ("Caption", 12, 16),
        ("Mono", 12, 18),
        ("MonoXs", 11, 14),
        ("Pill", 11, 14),
        ("TitleXl", 34, 50),
        ("TitleLg", 24, 35),
        ("TitleMd", 17, 25),
        ("TitleSm", 15, 22),
        ("ColumnHeader", 12, 16),
        ("Overline", 11, 16),
        ("JpBody", 19, 30),
        ("InspectorSource", 19, 32),
        ("JpRow", 13, 18),
        ("BodyLoose", 14, 22),
        ("ReviewSource", 19, 27),
        ("ReviewTranslation", 17, 30),
        ("ReviewSuggestion", 15, 24),
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
        resources["Vnf.MarkerHeight"] = Math.Round(BaseMarkerHeight * factor);
        resources["Vnf.MarkerLineHeight"] = Math.Round(BaseMarkerLineHeight * factor);
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
