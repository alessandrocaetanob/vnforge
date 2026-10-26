using System.Text.RegularExpressions;

namespace VNForge.Core.Projects;

/// <summary>Um id de linha (<c>yst00112:033792</c>) achado num texto.</summary>
/// <param name="Start">Posição do primeiro caractere do id no texto, em <c>char</c> UTF-16.</param>
/// <param name="Length">Tamanho do id no texto.</param>
/// <param name="ScriptKey">Chave do script (<c>yst00112</c>), a parte antes dos dois-pontos.</param>
/// <param name="UnitId">O id completo da unidade, como está em <c>text/*.json</c>.</param>
public sealed record LineIdMention(int Start, int Length, string ScriptKey, string UnitId);

/// <summary>
/// Reconhece ids de linha (chave do script, dois-pontos e a posição) em consultas e em mensagens de erro. Serve à busca do Editor e aos
/// links dos erros do build e da Conferência. A chave começa com letra ou sublinhado, o que deixa de fora horas como <c>20:19:08</c>.
/// </summary>
public static partial class LineIds
{
    [GeneratedRegex(@"(?<![\w:.\-])(?<key>[A-Za-z_][\w.\-]*):(?<position>\d+)(?![\w:])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();

    /// <summary>Os ids citados em <paramref name="text"/>, na ordem em que aparecem; vazio quando não há nenhum.</summary>
    public static IReadOnlyList<LineIdMention> Find(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(':', StringComparison.Ordinal))
        {
            return [];
        }

        var found = new List<LineIdMention>();
        foreach (Match match in Pattern().Matches(text))
        {
            found.Add(new LineIdMention(match.Index, match.Length, match.Groups["key"].Value, match.Value));
        }

        return found;
    }

    /// <summary>Verdadeiro quando <paramref name="text"/>, sem os espaços das pontas, é só um id completo (<c>script:posição</c>).</summary>
    public static bool TryParse(string? text, out LineIdMention id)
    {
        var trimmed = text?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && Find(trimmed) is [{ Start: 0 } only] && only.Length == trimmed.Length)
        {
            id = only;
            return true;
        }

        id = null!;
        return false;
    }
}
