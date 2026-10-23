using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

/// <summary>Faz o papel do <c>fold</c> (#61): troca <c>«</c>, <c>ª</c> e <c>ã</c> e recusa o resto fora do ASCII.</summary>
internal sealed class FakeFold : IReplacingTextEncoder
{
    private static readonly Dictionary<string, string> Map = new() { ["«"] = "\"", ["ª"] = "a", ["ã"] = "a" };

    public string Id => "fold";

    public string? GetReplacement(string character) => Map.GetValueOrDefault(character);

    public byte[] Encode(string text) => Encoding.ASCII.GetBytes(string.Concat(Characters(text).Select(c => Map.GetValueOrDefault(c, c))));

    public IReadOnlyList<int> FindUnencodable(string text)
    {
        var result = new List<int>();
        var index = 0;
        foreach (var c in Characters(text))
        {
            if (!Map.ContainsKey(c) && c.Any(ch => ch > 0x7F))
            {
                result.Add(index);
            }

            index += c.Length;
        }

        return result;
    }

    private static IEnumerable<string> Characters(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(index), out _, out var consumed);
            consumed = Math.Max(consumed, 1);
            yield return text.Substring(index, consumed);
            index += consumed;
        }
    }
}

/// <summary>Faz o papel do <c>cp1252</c> (#64): representa <c>«</c>, <c>ª</c> e <c>ã</c> como estão e recusa o que a página 1252 não tem (emoji).</summary>
internal sealed class FakeCp1252 : ITextEncoder
{
    public string Id => "cp1252";

    public byte[] Encode(string text) => Encoding.Latin1.GetBytes(text);

    public IReadOnlyList<int> FindUnencodable(string text)
    {
        var result = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) || text[i] > 0xFF)
            {
                result.Add(i);
                i += char.IsHighSurrogate(text[i]) ? 1 : 0;
            }
        }

        return result;
    }
}
