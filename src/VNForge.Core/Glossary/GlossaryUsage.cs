using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>
/// Quanto cada termo e cada personagem aparece nas linhas do projeto, para a página Glossário mostrar a contagem ao lado da linha da tabela.
/// Conta <b>linhas</b>, não ocorrências dentro da linha, e usa o mesmo critério do <see cref="GlossaryMatcher"/> (contido, ordinal, sem os espaços das pontas).
/// </summary>
public sealed class GlossaryUsage
{
    private readonly string[] sources;
    private readonly Dictionary<string, int> speakers;
    private readonly Dictionary<string, int> termCache = new(StringComparer.Ordinal);

    private GlossaryUsage(string[] sources, Dictionary<string, int> speakers)
    {
        this.sources = sources;
        this.speakers = speakers;
    }

    /// <summary>Projeto sem linhas: tudo conta zero.</summary>
    public static GlossaryUsage Empty { get; } = new([], new Dictionary<string, int>(CharacterBook.NameComparer));

    public static GlossaryUsage From(IEnumerable<TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);

        var sources = new List<string>();
        var speakers = new Dictionary<string, int>(CharacterBook.NameComparer);
        foreach (var unit in units)
        {
            sources.Add(unit.Source);
            if (!string.IsNullOrEmpty(unit.Speaker))
            {
                speakers[unit.Speaker] = speakers.GetValueOrDefault(unit.Speaker) + 1;
            }
        }

        return new GlossaryUsage([.. sources], speakers);
    }

    /// <summary>Quantas linhas têm <paramref name="term"/> no original. Termo vazio, ou só com espaços, nunca acerta.</summary>
    public int LinesContaining(string? term)
    {
        var key = term?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        // A tabela pergunta de novo a cada tecla; guardar a resposta evita varrer o projeto inteiro outra vez.
        if (!termCache.TryGetValue(key, out var count))
        {
            count = sources.Count(s => s.Contains(key, StringComparison.Ordinal));
            termCache[key] = count;
        }

        return count;
    }

    /// <summary>Quantas linhas têm <paramref name="speaker"/> como falante, pelo nome original extraído do jogo.</summary>
    public int LinesSpokenBy(string? speaker) =>
        string.IsNullOrEmpty(speaker?.Trim()) ? 0 : speakers.GetValueOrDefault(speaker.Trim());
}
