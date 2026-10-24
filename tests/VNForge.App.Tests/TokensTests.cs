using System.Text.Json;
using System.Text.RegularExpressions;

namespace VNForge.App.Tests;

public partial class TokensTests
{
    private static string Pascal(string kebab) =>
        string.Concat(kebab.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    [GeneratedRegex(@"x:Key=""(Vnf\.[A-Za-z0-9]+)""")]
    private static partial Regex KeyPattern();

    [Fact]
    public void Tokens_axaml_tem_todo_token_do_tokens_json_nos_dois_temas()
    {
        var baseDir = AppContext.BaseDirectory;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDir, "design", "tokens.json")));
        var axaml = File.ReadAllText(Path.Combine(baseDir, "design", "Tokens.axaml"));
        var chaves = KeyPattern().Matches(axaml).Select(m => m.Groups[1].Value).ToList();

        var esperados = new List<string>();
        foreach (var token in json.RootElement.GetProperty("color").GetProperty("tokens").EnumerateArray())
        {
            var nome = "Vnf." + Pascal(token.GetProperty("name").GetString()!);
            Assert.Equal(2, chaves.Count(c => c == nome));
            Assert.Equal(2, chaves.Count(c => c == nome + "Color"));
            esperados.Add(nome);
        }

        foreach (var grupo in new[] { "size", "spacing", "radius" })
        {
            foreach (var token in json.RootElement.GetProperty(grupo).GetProperty("tokens").EnumerateArray())
            {
                Assert.Contains("Vnf." + Pascal(token.GetProperty("name").GetString()!), chaves);
            }
        }

        Assert.NotEmpty(esperados);
    }
}
