using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>Uma razão do casamento: <paramref name="Strong"/> é a que concorda com o jogo (título da janela, pasta), pintada de verde; a fraca fica neutra.</summary>
public sealed record LibraryMatchBadge(string Text, bool Strong);

/// <summary>
/// Um candidato do diálogo "Qual é este jogo no VNDB?". <paramref name="Details"/> é a linha de baixo ("2021 · Curta · 4 h · nota 7,6"); o que o VNDB não deu fica de fora.
/// </summary>
public sealed record LibraryMatchChoice(string VndbId, string Title, string? OriginalTitle, string Details, VndbImageRef? Image, IReadOnlyList<LibraryMatchBadge> Badges);

/// <summary>Descreve os candidatos que o casamento guardou, na ordem da pontuação.</summary>
public static class LibraryMatchChoices
{
    /// <summary>Pontuação a partir da qual a pasta conta como a mesma do jogo; abaixo dela é "Pasta parecida".</summary>
    public const double StrongScore = 0.8;

    public static IReadOnlyList<LibraryMatchChoice> Describe(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return [.. game.Candidates.Select(c => Describe(game, c))];
    }

    public static LibraryMatchChoice Describe(LibraryGame game, LibraryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(candidate);
        var details = new List<string>();
        if (LibraryDrawers.YearOf(candidate.Released) is { } year)
        {
            details.Add(year);
        }

        if (LibraryLength.Short(candidate.LengthMinutes, candidate.Length) is { } length)
        {
            details.Add(length);
        }

        if (VndbRating.Label(candidate.Rating) is { } rating)
        {
            details.Add(rating);
        }

        var strong = candidate.Score >= StrongScore;
        var badges = candidate.Evidence.Select(e => e switch
        {
            "janela" => new LibraryMatchBadge("Título da janela", true),
            "pasta" => strong ? new LibraryMatchBadge("Pasta", true) : new LibraryMatchBadge("Pasta parecida", false),
            _ => new LibraryMatchBadge($"Exe: {Path.GetFileName(game.Exe) ?? e}", false),
        }).ToList();
        if (badges.Count == 0)
        {
            badges.Add(new LibraryMatchBadge("Só o título", false));
        }

        return new LibraryMatchChoice(candidate.VndbId, candidate.Title, candidate.OriginalTitle, string.Join(" · ", details), candidate.Image, badges);
    }
}
