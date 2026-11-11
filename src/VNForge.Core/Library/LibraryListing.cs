using System.Globalization;
using System.Text;
using VNForge.Core.Projects;

namespace VNForge.Core.Library;

/// <summary>Os filtros em chip da Biblioteca. Um jogo conta em "Todos" e em quantos dos outros ele cumpre.</summary>
public enum LibraryFilter
{
    /// <summary>Todos os jogos.</summary>
    All,

    /// <summary>Tem projeto de tradução e ele ainda não chegou a 100%.</summary>
    InTranslation,

    /// <summary>Sem projeto e sem tradução completa em português no VNDB.</summary>
    NoPortuguese,

    /// <summary>A engine foi reconhecida por assinatura de arquivo mas o VNForge ainda não a traduz. Engine que nem isso foi reconhecida não entra.</summary>
    Unsupported,

    /// <summary>A engine é uma que o VNForge traduz.</summary>
    Supported,
}

/// <summary>A contagem de cada chip de filtro.</summary>
public sealed record LibraryCounts(int All, int InTranslation, int NoPortuguese, int Unsupported, int Supported = 0)
{
    public int Of(LibraryFilter filter) => filter switch
    {
        LibraryFilter.InTranslation => InTranslation,
        LibraryFilter.NoPortuguese => NoPortuguese,
        LibraryFilter.Unsupported => Unsupported,
        LibraryFilter.Supported => Supported,
        _ => All,
    };
}

/// <summary>Filtra, conta e busca nos jogos da Biblioteca, sem depender da tela nem do disco: o andamento de cada projeto vem de quem chama.</summary>
public static class LibraryListing
{
    /// <summary>O jogo cumpre o filtro. <paramref name="percent"/> é a parte traduzida do projeto ligado (0 a 100), nulo quando o jogo não tem projeto ou a pasta dele não se lê.</summary>
    public static bool Matches(LibraryGame game, LibraryFilter filter, int? percent)
    {
        ArgumentNullException.ThrowIfNull(game);
        return filter switch
        {
            LibraryFilter.InTranslation => game.ProjectPath is not null && (percent ?? 0) < 100,
            LibraryFilter.NoPortuguese => game.ProjectPath is null && game.CurrentPortuguese?.Status != PortugueseStatus.Complete,
            LibraryFilter.Unsupported => game.Engine.Source == LibraryEngineSource.Signature,
            LibraryFilter.Supported => game.Engine.Translatable,
            _ => true,
        };
    }

    public static LibraryCounts Count(IEnumerable<LibraryGame> games, Func<LibraryGame, int?> percentOf)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(percentOf);
        int all = 0, inTranslation = 0, noPortuguese = 0, unsupported = 0, supported = 0;
        foreach (var game in games)
        {
            var percent = percentOf(game);
            all++;
            inTranslation += Matches(game, LibraryFilter.InTranslation, percent) ? 1 : 0;
            noPortuguese += Matches(game, LibraryFilter.NoPortuguese, percent) ? 1 : 0;
            unsupported += Matches(game, LibraryFilter.Unsupported, percent) ? 1 : 0;
            supported += Matches(game, LibraryFilter.Supported, percent) ? 1 : 0;
        }

        return new LibraryCounts(all, inTranslation, noPortuguese, unsupported, supported);
    }

    /// <summary>
    /// Os jogos que cumprem o filtro e a busca, na ordem de <paramref name="games"/>. A busca é por palavra: todas as palavras têm de aparecer no título, no título original, no da janela,
    /// no nome da pasta ou numa tag (as do VNDB que a privacidade deixa ver e as da pessoa), sem diferenciar maiúsculas nem acentos.
    /// </summary>
    public static IReadOnlyList<LibraryGame> Filter(IEnumerable<LibraryGame> games, LibraryFilter filter, string? query, Func<LibraryGame, int?> percentOf, UserTags? userTags, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(percentOf);
        ArgumentNullException.ThrowIfNull(privacy);
        var words = Words(query);
        return
        [
            .. games.Where(g => Matches(g, filter, percentOf(g)) && (words.Length == 0 || ContainsAll(Haystack(g, userTags, privacy), words))),
        ];
    }

    /// <summary>"18.802 de 48.211 linhas prontas" é o texto do andamento da gaveta, com o ponto dos milhares do pt-BR.</summary>
    public static string ReadyLines(StatusCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return $"{Number(counts.Ready)} de {Number(counts.Total)} linhas prontas";
    }

    private static string Number(int value) => value.ToString("N0", NumberFormatInfo.InvariantInfo).Replace(',', '.');

    private static string Haystack(LibraryGame game, UserTags? userTags, LibraryPrivacy privacy)
    {
        var parts = new List<string?> { game.Title, game.Info?.Title, game.Info?.OriginalTitle, game.WindowTitle, GameLibrary.FolderName(game) };
        parts.AddRange(LibraryVisibility.VisibleTags(game, privacy).Select(t => t.Name));
        if (userTags is not null)
        {
            parts.AddRange(userTags.TagsOf(game.Id));
        }

        return Fold(string.Join('\n', parts.Where(p => !string.IsNullOrEmpty(p))));
    }

    private static string[] Words(string? query) =>
        string.IsNullOrWhiteSpace(query) ? [] : Fold(query).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool ContainsAll(string haystack, string[] words) => words.All(w => haystack.Contains(w, StringComparison.Ordinal));

    /// <summary>Minúsculas e sem acento: "Yūgure" casa com "yugure". Só tira o sinal de letra latina: a marca de uma letra japonesa (が) muda a letra e fica.</summary>
    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        var latin = false;
        foreach (var c in text)
        {
            var mark = CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;
            if (mark && latin)
            {
                continue;
            }

            latin = !mark && c < 'ɐ';
            if (latin)
            {
                foreach (var part in c.ToString().Normalize(NormalizationForm.FormD).Where(p => CharUnicodeInfo.GetUnicodeCategory(p) != UnicodeCategory.NonSpacingMark))
                {
                    builder.Append(char.ToLowerInvariant(part));
                }
            }
            else
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
