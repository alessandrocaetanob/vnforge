using System.Globalization;
using System.Text;

namespace VNForge.Core.Text;

/// <summary>Um caractere de uma tradução que a estratégia não grava como está.</summary>
/// <param name="UnitId">Id da linha (<c>yst00012:000034</c>).</param>
/// <param name="ScriptKey">Chave do script (<c>yst00012</c>), a de <c>text/&lt;chave&gt;.json</c>.</param>
/// <param name="Position">Posição do caractere na tradução, em <c>char</c> do UTF-16, a partir de 0.</param>
/// <param name="Character">O caractere.</param>
/// <param name="Replacement">O que a estratégia grava no lugar; <see langword="null"/> quando ela não representa o caractere e o build recusa.</param>
public sealed record EncodingIssue(string UnitId, string ScriptKey, int Position, string Character, string? Replacement)
{
    /// <summary>Ponto de código no formato <c>U+00AB</c>.</summary>
    public string CodePoint => "U+" + (Character.Length == 2 && char.IsSurrogatePair(Character, 0) ? char.ConvertToUtf32(Character, 0) : Character[0]).ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>Verdadeiro quando a estratégia não representa o caractere: o build não grava a linha.</summary>
    public bool IsBlocking => Replacement is null;
}

/// <summary>Resultado de <see cref="EncodingReporter.Create"/>: todos os caracteres que a estratégia troca ou recusa, na ordem do script e da linha.</summary>
/// <param name="Strategy">Id da estratégia analisada.</param>
/// <param name="CheckedLines">Unidades com tradução a gravar (<see cref="TextUnit.NeedsRewrite"/>) que foram conferidas.</param>
/// <param name="Issues">Um item por ocorrência.</param>
public sealed record EncodingReport(string Strategy, int CheckedLines, IReadOnlyList<EncodingIssue> Issues)
{
    /// <summary>Ocorrências que impedem o build.</summary>
    public IReadOnlyList<EncodingIssue> Blocking { get; } = Issues.Where(i => i.IsBlocking).ToList();

    /// <summary>Ocorrências que a estratégia troca por um substituto.</summary>
    public IReadOnlyList<EncodingIssue> Replaced { get; } = Issues.Where(i => !i.IsBlocking).ToList();

    /// <summary>Verdadeiro quando alguma linha não pode ser gravada.</summary>
    public bool HasBlocking => Issues.Any(i => i.IsBlocking);
}

/// <summary>Monta o <see cref="EncodingReport"/> de um conjunto de traduções para um <see cref="ITextEncoder"/>.</summary>
public static class EncodingReporter
{
    /// <summary>Percorre as unidades com <see cref="TextUnit.NeedsRewrite"/> e junta todos os casos, sem parar no primeiro.</summary>
    /// <param name="texts">Unidades por chave de script (<c>ProjectFolder.LoadAllText</c>).</param>
    /// <param name="encoder">Estratégia do projeto.</param>
    public static EncodingReport Create(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(encoder);

        var issues = new List<EncodingIssue>();
        var checkedLines = 0;
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var unit in units.Where(u => u.NeedsRewrite))
            {
                checkedLines++;
                var text = unit.Translation!;
                var blocked = encoder.FindUnencodable(text).ToHashSet();
                foreach (var (index, character) in Characters(text))
                {
                    var replacement = !blocked.Contains(index) && encoder is IReplacingTextEncoder replacing ? replacing.GetReplacement(character) : null;
                    if (blocked.Contains(index) || replacement is not null)
                    {
                        issues.Add(new EncodingIssue(unit.Id, key, index, character, replacement));
                    }
                }
            }
        }

        return new EncodingReport(encoder.Id, checkedLines, issues);
    }

    private static IEnumerable<(int Index, string Character)> Characters(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(index), out _, out var consumed);
            consumed = Math.Max(consumed, 1);
            yield return (index, text.Substring(index, consumed));
            index += consumed;
        }
    }
}
