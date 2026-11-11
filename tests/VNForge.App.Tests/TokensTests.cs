using System.Text.Json;
using System.Text.RegularExpressions;

namespace VNForge.App.Tests;

public partial class TokensTests
{
    private static string Pascal(string kebab) =>
        string.Concat(kebab.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Segue referências como <c>{accent-soft}</c> até o hexno tema pedido.</summary>
    private static string Resolve(JsonElement root, string value, string theme) => value.StartsWith('{')
        ? root.GetProperty("color").GetProperty("tokens").EnumerateArray()
            .Where(t => t.GetProperty("name").GetString() == value.Trim('{', '}'))
            .Select(t => Resolve(root, t.GetProperty("value").GetProperty(theme).GetString()!, theme))
            .First()
        : value;

    [GeneratedRegex(@"x:Key=""(Vnf\.[A-Za-z0-9]+)""")]
    private static partial Regex KeyPattern();
}
