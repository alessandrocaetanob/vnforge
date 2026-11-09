using System.Text.Json;

namespace VNForge.Cli.Tests;

/// <summary>A paleta da CLI repete à mão os tokens do tema escuro do design system: o teste confere cada cor com o tokens.json para a deriva não passar de novo.</summary>
public class CliPaletteTokensTests
{
    public static TheoryData<string, string> Mapping => new()
    {
        { nameof(CliPalette.Approved), "seg-approved" },
        { nameof(CliPalette.Reviewed), "seg-reviewed" },
        { nameof(CliPalette.Translated), "seg-translated" },
        { nameof(CliPalette.Ai), "seg-ai" },
        { nameof(CliPalette.Pending), "seg-pending" },
        { nameof(CliPalette.Warning), "signal-warning-fg" },
        { nameof(CliPalette.Error), "signal-error-fg" },
        { nameof(CliPalette.Accent), "accent" },
        { nameof(CliPalette.TextboxBackground), "textbox-bg" },
        { nameof(CliPalette.TextboxInk), "textbox-ink" },
        { nameof(CliPalette.TextboxOverflow), "textbox-overflow" },
        { nameof(CliPalette.TextboxPlate), "textbox-plate" },
    };

    [Theory]
    [MemberData(nameof(Mapping))]
    public void Cor_da_paleta_e_a_do_token_no_tema_escuro(string color, string token)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "tokens.json")));
        var entry = json.RootElement.GetProperty("color").GetProperty("tokens").EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == token);
        var expected = entry.GetProperty("value").GetProperty("dark").GetString()!;
        Assert.DoesNotContain('{', expected);

        var actual = typeof(CliPalette).GetField(color)!.GetValue(null)!;
        Assert.Equal(expected, CliPalette.Hex((Spectre.Console.Color)actual), ignoreCase: true);
    }
}
