using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>
/// Acha qual edição do VNDB é a da pasta de um jogo. Compara o que a pasta diz (título da janela, nome da pasta e do executável, como no casamento do jogo) com os títulos
/// de cada edição, nos idiomas dela. Vale a edição que mais se parece, e só se passar de <see cref="MinimumScore"/>; edições empatadas valem todas. Patch de tradução não
/// é a edição de uma pasta de jogo e fica de fora.
/// </summary>
public static class LibraryEditionMatcher
{
    /// <summary>O mínimo de semelhança (0 a 1) para dizer que a edição é a da pasta.</summary>
    public const double MinimumScore = 0.8;

    private const double Tie = 0.0001;

    /// <summary>Os ids das edições de <paramref name="releases"/> que casam com a pasta do jogo; vazio quando nenhuma se parece o bastante.</summary>
    public static IReadOnlyList<string> Match(LibraryGame game, IEnumerable<VndbRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(releases);
        var terms = GameNames.SearchTerms(game.WindowTitle, game.Exe, game.Folder);
        if (terms.Count == 0 || game.VndbId is null)
        {
            return [];
        }

        var scored = releases
            .Where(r => !r.Patch && r.VisualNovels.Any(v => v.VisualNovelId == game.VndbId))
            .Select(r => (r.Id, Score: Score(r, terms)))
            .Where(s => s.Score >= MinimumScore)
            .ToList();
        if (scored.Count == 0)
        {
            return [];
        }

        var best = scored.Max(s => s.Score);
        return [.. scored.Where(s => s.Score >= best - Tie).Select(s => s.Id).Order(StringComparer.Ordinal)];
    }

    private static double Score(VndbRelease release, IReadOnlyList<SearchTerm> terms)
    {
        var titles = new List<string> { release.Title };
        if (release.AltTitle is { } alt)
        {
            titles.Add(alt);
        }

        foreach (var language in release.Languages)
        {
            titles.AddRange(new[] { language.Title, language.Latin }.OfType<string>());
        }

        return terms.Max(term => BestMatch(term.Text, titles));
    }

    private static double BestMatch(string term, List<string> titles)
    {
        return titles.Max(title => TitleSimilarity.Between(term, title));
    }
}
