using VNForge.Core.Glossary;

namespace VNForge.Core.Spelling;

/// <summary>
/// As palavras que o corretor aceita além do dicionário pt-BR: as das traduções do Glossário e as dos nomes dos personagens (<c>glossary.json</c> e
/// <c>characters.json</c>). Gerado do projeto a cada conferência; nada é gravado em disco. A comparação ignora maiúsculas.
/// </summary>
public sealed class ProjectDictionary
{
    private static readonly char[] PartSeparators = ['-', '\''];

    private readonly HashSet<string> words = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dicionário sem nenhuma palavra do projeto.</summary>
    public static ProjectDictionary Empty { get; } = new();

    /// <summary>Quantas palavras o projeto acrescenta.</summary>
    public int Count => words.Count;

    /// <summary>As traduções dos termos do glossário, dos personagens e dos tratamentos entre eles ("Ken-chan"), palavra por palavra.</summary>
    public static ProjectDictionary Create(IEnumerable<GlossaryTerm> glossary, IEnumerable<CharacterEntry> characters, IEnumerable<AddressTerm>? addressTerms = null)
    {
        ArgumentNullException.ThrowIfNull(glossary);
        ArgumentNullException.ThrowIfNull(characters);

        var dictionary = new ProjectDictionary();
        foreach (var term in glossary)
        {
            dictionary.AddPhrase(term.Target);
        }

        foreach (var character in characters)
        {
            dictionary.AddPhrase(character.Target);
        }

        foreach (var term in addressTerms ?? [])
        {
            dictionary.AddPhrase(term.Target);
        }

        return dictionary;
    }

    /// <summary>Verdadeiro quando o projeto conhece a <paramref name="word"/>.</summary>
    public bool Contains(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return words.Contains(word);
    }

    /// <summary>Acrescenta cada palavra de <paramref name="phrase"/> (separada por qualquer coisa que não seja letra, hífen ou apóstrofo).</summary>
    internal void AddPhrase(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return;
        }

        var start = -1;
        for (var i = 0; i <= phrase.Length; i++)
        {
            var inWord = i < phrase.Length && (char.IsLetter(phrase[i]) || (start >= 0 && phrase[i] is '-' or '\'' or '’'));
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                AddWord(phrase[start..i]);
                start = -1;
            }
        }
    }

    internal void AddWord(string word)
    {
        var trimmed = word.Trim('-', '\'', '’').Replace('’', '\'');
        if (trimmed.Length >= 2)
        {
            words.Add(trimmed);
            foreach (var part in trimmed.Split(PartSeparators))
            {
                if (part.Length >= 2)
                {
                    words.Add(part);
                }
            }
        }
    }
}
