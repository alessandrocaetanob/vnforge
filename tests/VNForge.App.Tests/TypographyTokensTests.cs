using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VNForge.App.Services;

namespace VNForge.App.Tests;

/// <summary>
/// O Generate-Tokens.ps1 não gera a escala de texto: ela vive em duas cópias escritas à mão (<c>Themes/TextScale.axaml</c> e <c>TextAppearance.Scales</c>).
/// Este teste compara as duas com <c>type.groups</c> do tokens.json para a deriva não passar despercebida.
/// </summary>
public partial class TypographyTokensTests
{
    [GeneratedRegex(@"x:Key=""Vnf\.(FontSize|LineHeight)\.([A-Za-z0-9]+)"">([\d.]+)<")]
    private static partial Regex ScaleEntry();

    private static string Pascal(string kebab) =>
        string.Concat(kebab.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>
    /// Alturas de linha em que o app se afasta do tokens.json: a Zen Kaku Gothic New pede 1,45 de altura por corpo (34,75 em 24 px e 24,6 em 17 px)
    /// e abaixo disso o TextBlock corta as descendentes. Se o design passar a caber, a exceção sai daqui e das duas cópias.
    /// </summary>
    private static readonly Dictionary<string, double> LineHeightExceptions = new() { ["TitleLg"] = 35, ["TitleMd"] = 25 };

    private static double Px(JsonElement style, string property) =>
        double.Parse(style.GetProperty(property).GetString()!.Replace("px", string.Empty), CultureInfo.InvariantCulture);

    /// <summary>Estilos do tokens.json que o app lê pela escala. <c>body-strong</c> repete o <c>body</c> e a <c>textbox</c> tem as medidas do jogo, não as da interface.</summary>
    private static List<(string Name, double Size, double Line)> TokenStyles()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "tokens.json")));
        return json.RootElement.GetProperty("type").GetProperty("groups").EnumerateArray()
            .SelectMany(g => g.GetProperty("styles").EnumerateArray())
            .Where(s => s.GetProperty("name").GetString() is not ("body-strong" or "textbox"))
            .Select(s => (Pascal(s.GetProperty("name").GetString()!), Px(s, "fontSize"), Px(s, "lineHeight")))
            .ToList();
    }

    [Fact]
    public void Scales_tem_a_tipografia_do_tokens_json()
    {
        var scales = TextAppearance.Scales.ToDictionary(s => s.Name);
        var styles = TokenStyles();
        Assert.NotEmpty(styles);
        foreach (var (name, size, line) in styles)
        {
            Assert.True(scales.TryGetValue(name, out var s), $"{name} falta em TextAppearance.Scales");
            Assert.Equal((size, LineHeightExceptions.GetValueOrDefault(name, line)), (s.Size, s.Line));
        }
    }

    [Fact]
    public void TextScale_axaml_tem_a_tipografia_do_tokens_json_e_o_mesmo_que_Scales()
    {
        var axaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "TextScale.axaml"));
        var values = ScaleEntry().Matches(axaml)
            .ToDictionary(m => (m.Groups[1].Value, m.Groups[2].Value), m => double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));

        foreach (var (name, size, line) in TokenStyles())
        {
            Assert.Equal(size, values[("FontSize", name)]);
            Assert.Equal(LineHeightExceptions.GetValueOrDefault(name, line), values[("LineHeight", name)]);
        }

        foreach (var (name, size, line) in TextAppearance.Scales)
        {
            Assert.Equal(size, values[("FontSize", name)]);
            Assert.Equal(line, values[("LineHeight", name)]);
        }
    }
}
