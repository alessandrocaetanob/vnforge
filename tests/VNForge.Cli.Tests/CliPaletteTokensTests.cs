using System.Text.Json;

namespace VNForge.Cli.Tests;

/// <summary>A paleta da CLI repete à mão os tokens do tema escuro do design system: o teste confere cada cor com o tokens.json para a deriva não passar de novo.</summary>
public class CliPaletteTokensTests
{
    private static readonly (string Color, string Token)[] Mapping =
    [
        (nameof(CliPalette.Approved), "seg-approved"),
        (nameof(CliPalette.Reviewed), "seg-reviewed"),
        (nameof(CliPalette.Translated), "seg-translated"),
        (nameof(CliPalette.Ai), "seg-ai"),
        (nameof(CliPalette.Pending), "seg-pending"),
        (nameof(CliPalette.Warning), "signal-warning-fg"),
        (nameof(CliPalette.Error), "signal-error-fg"),
        (nameof(CliPalette.Accent), "accent"),
        (nameof(CliPalette.TextboxBackground), "textbox-bg"),
        (nameof(CliPalette.TextboxInk), "textbox-ink"),
        (nameof(CliPalette.TextboxOverflow), "textbox-overflow"),
        (nameof(CliPalette.TextboxPlate), "textbox-plate"),
    ];

    [Fact]
    public void Cada_cor_da_paleta_e_a_do_token_no_tema_escuro()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "tokens.json")));
        var tokens = json.RootElement.GetProperty("color").GetProperty("tokens").EnumerateArray().ToList();
        var problems = new List<string>();
        foreach (var (color, token) in Mapping)
        {
            var entries = tokens.Where(t => t.GetProperty("name").GetString() == token).ToList();
            if (entries.Count != 1)
            {
                problems.Add($"{color}: o token '{token}' aparece {entries.Count} vez(es) no tokens.json; esperava 1.");
                continue;
            }

            var field = typeof(CliPalette).GetField(color);
            if (field is null)
            {
                problems.Add($"{color}: o campo não existe mais em CliPalette (token '{token}').");
                continue;
            }

            var expected = entries[0].GetProperty("value").GetProperty("dark").GetString()!;
            var actual = CliPalette.Hex((Spectre.Console.Color)field.GetValue(null)!);
            if (expected.Contains('{', StringComparison.Ordinal) || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{color}: o token '{token}' vale '{expected}' e a paleta tem '{actual}'.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
