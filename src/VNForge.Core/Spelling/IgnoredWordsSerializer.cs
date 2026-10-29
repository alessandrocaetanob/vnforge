using VNForge.Core.Glossary;

namespace VNForge.Core.Spelling;

/// <summary>Lê e grava <c>ignored-words.json</c>: o envelope <c>{ "version": 1, "entries": [...] }</c> com um <see cref="IgnoredWord"/> por linha, na ordem recebida.</summary>
public static class IgnoredWordsSerializer
{
    /// <exception cref="GlossaryFileException">Palavra vazia ou repetida, ou texto com UTF-16 inválido.</exception>
    public static string Serialize(IReadOnlyList<IgnoredWord> words, string filePath = "(memória)") =>
        EntryFile.Serialize(words, filePath, Check, w => w.Word);

    /// <exception cref="GlossaryFileException">JSON malformado, envelope fora do formato, campo desconhecido ou ausente, versão desconhecida, palavra vazia ou repetida.</exception>
    public static IReadOnlyList<IgnoredWord> Parse(string json, string filePath) =>
        EntryFile.Parse<IgnoredWord>(json, filePath, Check);

    /// <summary>Como duas palavras são comparadas: sem espaços nas pontas e com o apóstrofo curvo igual ao reto (o corretor trata os dois como um só).</summary>
    internal static string KeyOf(string word) => word.Trim().Replace('’', '\'');

    private static void Check(IReadOnlyList<IgnoredWord> words, string filePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < words.Count; i++)
        {
            var word = (words[i] ?? throw new GlossaryFileException(filePath, $"$.entries[{i}]: a entrada é nula.")).Word;
            if (string.IsNullOrWhiteSpace(word))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].word: a palavra não pode ser vazia.");
            }

            if (!seen.Add(KeyOf(word)))
            {
                throw new GlossaryFileException(filePath, $"'{word.Trim()}' aparece mais de uma vez no arquivo.");
            }
        }
    }
}
