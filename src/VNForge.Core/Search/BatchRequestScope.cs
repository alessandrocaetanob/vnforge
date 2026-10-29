using System.Text.RegularExpressions;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Search;

/// <summary>Uma fala que o pedido pode alcançar: onde ela está e o texto da linha.</summary>
/// <param name="ScriptKey">Chave do script.</param>
/// <param name="Number">Posição da linha no script, a partir de 1.</param>
/// <param name="Unit">A linha como está agora.</param>
public sealed record BatchCandidate(string ScriptKey, int Number, TextUnit Unit);

/// <summary>
/// O que um pedido em lote escrito em português diz sobre quais falas ele alcança: os personagens citados, as cenas citadas e a palavra a trocar.
/// Serve para a IA só ler as falas que podem mudar, em vez do projeto inteiro: cada filtro que o pedido traz reduz o texto enviado e o gasto.
/// Os filtros se somam (o personagem E a cena E a palavra); sem nenhum, o pedido alcança todas as falas traduzidas.
/// </summary>
/// <param name="Speakers">O original do nome (<see cref="CharacterEntry.Source"/>) de cada personagem citado.</param>
/// <param name="Scripts">As chaves dos scripts citados.</param>
/// <param name="Terms">A palavra ou frase a trocar, como a pessoa a escreveu; a comparação ignora acento e maiúsculas e exige a palavra inteira.</param>
public sealed partial record BatchRequestScope(IReadOnlyList<string> Speakers, IReadOnlyList<string> Scripts, IReadOnlyList<string> Terms)
{
    private const int MinNameWordLength = 3;

    /// <summary>O pedido cita personagem, cena ou palavra; falso quando ele alcança todas as falas traduzidas.</summary>
    public bool IsNarrowed => Speakers.Count + Scripts.Count + Terms.Count > 0;

    /// <summary>Lê o pedido. Nada é inventado: o que o pedido não cita fica sem filtro, e a IA decide sobre as falas que chegarem a ela.</summary>
    public static BatchRequestScope Parse(string request, IReadOnlyList<CharacterEntry> characters, IEnumerable<string> scriptKeys)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(scriptKeys);
        var folded = SearchText.Fold(request);

        var terms = TermsOf(request);
        var foldedTerms = terms.Select(SearchText.Fold).ToList();

        // "trocar Kenta por Kento": o nome é a palavra a trocar, não o falante; contá-lo como falante perderia as falas dos outros que o citam.
        var speakers = characters
            .Where(c => Names(c).Any(name => ContainsWord(folded, SearchText.Fold(name))) && !Names(c).Any(name => foldedTerms.Contains(SearchText.Fold(name).Trim())))
            .Select(c => c.Source)
            .Distinct(CharacterBook.NameComparer)
            .ToList();
        var scripts = scriptKeys
            .Where(key => key.Length > 0 && ContainsWord(folded, SearchText.Fold(key)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new BatchRequestScope(speakers, scripts, terms);
    }

    /// <summary>
    /// As falas traduzidas que o pedido alcança, na ordem ordinal da chave do script e na ordem do arquivo. Linha sem tradução nunca entra:
    /// o pedido reescreve tradução, não traduz.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado; o token é conferido a cada linha.</exception>
    public IReadOnlyList<BatchCandidate> Select(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, IReadOnlyList<CharacterEntry> characters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(characters);
        var book = new CharacterBook(characters);
        var wantedSpeakers = Speakers.ToHashSet(CharacterBook.NameComparer);
        var foldedTerms = Terms.Select(SearchText.Fold).ToList();
        var candidates = new List<BatchCandidate>();
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (Scripts.Count > 0 && !Scripts.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = units[i];
                if (!unit.HasTranslation || !SpeakerWanted(unit, book, wantedSpeakers) || !TermWanted(unit, foldedTerms))
                {
                    continue;
                }

                candidates.Add(new BatchCandidate(key, i + 1, unit));
            }
        }

        return candidates;
    }

    private bool SpeakerWanted(TextUnit unit, CharacterBook book, HashSet<string> wanted)
    {
        if (Speakers.Count == 0)
        {
            return true;
        }

        return book.Find(unit.EffectiveSpeaker) is { } entry && wanted.Contains(entry.Source);
    }

    private static bool TermWanted(TextUnit unit, List<string> foldedTerms)
    {
        if (foldedTerms.Count == 0)
        {
            return true;
        }

        var text = SearchText.Fold(unit.Translation ?? string.Empty);
        return foldedTerms.Any(term => ContainsWord(text, term));
    }

    /// <summary>O nome inteiro e cada palavra de 3 letras ou mais dele: "Misaki Sakura" responde a "Sakura".</summary>
    private static IEnumerable<string> Names(CharacterEntry character)
    {
        yield return character.Source;
        yield return character.Target;
        foreach (var word in character.Target.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= MinNameWordLength)
            {
                yield return word;
            }
        }
    }

    private static List<string> TermsOf(string request)
    {
        var terms = new List<string>();
        foreach (var pattern in new[] { SwapPattern(), RemovePattern() })
        {
            foreach (var groups in pattern.Matches(request).Select(match => match.Groups))
            {
                var term = (groups["q"].Success ? groups["q"].Value : groups["x"].Value).Trim();
                if (term.Length > 0 && (groups["q"].Success || IsPlausibleTerm(term)) && !terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                {
                    terms.Add(term);
                }
            }
        }

        return terms;
    }

    private static readonly HashSet<string> MetaWords = new(StringComparer.Ordinal)
    {
        "fala", "falas", "frase", "frases", "linha", "linhas", "texto", "textos", "dialogo", "dialogos", "cena", "cenas",
        "personagem", "personagens", "tom", "estilo", "jeito", "tudo", "traducao", "traducoes",
    };

    private static readonly HashSet<string> Prepositions = new(StringComparer.Ordinal) { "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas" };

    /// <summary>
    /// Uma palavra ou expressão curta que alguém trocaria no texto. "As falas do Kenta" (em "mudar as falas do Kenta para o tom formal") descreve onde ou como,
    /// não o que trocar: sem aspas, esses pedidos não viram termo literal e a IA decide sobre as falas que o resto do pedido alcança.
    /// </summary>
    private static bool IsPlausibleTerm(string term)
    {
        var words = SearchText.Fold(term).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && !Prepositions.Contains(words[0]) && !words.Any(MetaWords.Contains);
    }

    /// <summary>A palavra aparece em <paramref name="text"/> sem letra nem número colados nas pontas; ambos já sem acento e em minúsculas.</summary>
    private static bool ContainsWord(string text, string word)
    {
        word = word.Trim();
        if (word.Length == 0)
        {
            return false;
        }

        var start = 0;
        while ((start = text.IndexOf(word, start, StringComparison.Ordinal)) >= 0)
        {
            var end = start + word.Length;
            if (!IsLatinWord(word) || (!IsWordChar(text, start - 1) && !IsWordChar(text, end)))
            {
                return true;
            }

            start++;
        }

        return false;
    }

    // Kana e kanji são todos letras e nunca ficam separados: nome em japonês vale como trecho, sem checar a vizinhança.
    private static bool IsLatinWord(string word) => word.All(c => c < 'ɐ');

    private static bool IsWordChar(string text, int index) => index >= 0 && index < text.Length && char.IsLetterOrDigit(text[index]);

    private const string Quoted = """(?:"(?<q>[^"]+)"|“(?<q>[^”]+)”|'(?<q>[^']+)'|‘(?<q>[^’]+)’|«(?<q>[^»]+)»)""";

    [GeneratedRegex("\\b(?:trocar|troque|substituir|substitua|mudar|mude|corrigir|corrija)\\s+(?:(?:o|a|os|as|todo|todos|todas)\\s+)?(?:" + Quoted + "|(?<x>[^\\s\"“”'‘’«»]+(?:\\s+[^\\s\"“”'‘’«»]+){0,3}?))\\s+(?:por|para|pelo|pela|pelos|pelas|com)\\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SwapPattern();

    [GeneratedRegex("\\b(?:tirar|retirar|remover|apagar|cortar)\\s+(?:(?:o|a|os|as)\\s+)?" + Quoted, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RemovePattern();
}
