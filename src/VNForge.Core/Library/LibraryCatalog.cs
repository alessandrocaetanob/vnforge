using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>
/// Como gerar o catálogo. <paramref name="Privacy"/> decide o que sai escondido (spoiler e conteúdo adulto, como na Biblioteca). <paramref name="IncludeNotes"/> põe as notas de
/// texto que a pessoa escreveu em cada jogo; ficam de fora por padrão, porque o catálogo é feito para ser mostrado.
/// </summary>
public sealed record LibraryCatalogOptions(LibraryPrivacy Privacy, bool IncludeNotes = false);

/// <summary>A página gerada. <paramref name="Games"/> são os jogos que entraram; <paramref name="Skipped"/> são os que ficaram de fora por ainda não terem jogo do VNDB confirmado.</summary>
public sealed record LibraryCatalogResult(string Html, int Games, int Skipped);

/// <summary>A duração do jogo como a Biblioteca a escreve, em português.</summary>
public static class LibraryLength
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Os minutos votados no VNDB quando há, senão a faixa de 1 a 5 do VNDB; <c>sem votos</c> quando não há nenhum dos dois.</summary>
    public static string Text(int? minutes, int? category)
    {
        if (minutes is { } m)
        {
            return m >= 60 ? $"cerca de {(m / 60.0).ToString("0", PortugueseBrazil)} h" : $"{m} min";
        }

        return category switch
        {
            1 => "muito curta (menos de 2 h)",
            2 => "curta (2 a 10 h)",
            3 => "média (10 a 30 h)",
            4 => "longa (30 a 50 h)",
            5 => "muito longa (mais de 50 h)",
            _ => "sem votos",
        };
    }
}

/// <summary>
/// O catálogo exportável (d10, #555): uma página HTML só, sem script, com as VNs da Biblioteca que têm jogo do VNDB confirmado. Cada jogo leva a ficha (capa, sinopse, tags, duração e a nota do VNDB),
/// o que a pessoa jogou (<see cref="PlayLog"/>), a nota e os rótulos dela, e quanto traduziu (o projeto ligado pelo <see cref="LibraryProjectLinker"/>). A sinopse e os personagens vêm de
/// <see cref="ILibrarySheetSource"/> e, sem ficha traduzida, é o texto original do VNDB que vale. Imagem nunca é copiada: entra como endereço do VNDB (HTTPS em <c>vndb.org</c>, conferido por
/// <see cref="VndbImageVisibility.IsAllowedSource"/>), e a página declara uma política de conteúdo que só deixa imagem do VNDB carregar. Todo texto é escapado. O rodapé dá o crédito ao VNDB e à ODbL.
/// O documento é XML bem formado (elementos vazios fechados), para abrir em qualquer navegador e passar por um leitor XML nos testes.
/// </summary>
public static partial class LibraryCatalog
{
    private const string VndbHome = "https://vndb.org/";
    private const string OdblUrl = "https://opendatacommons.org/licenses/odbl/1-0/";
    private const string DatabaseLicenseUrl = "https://vndb.org/d17";
    private const int RegexTimeoutMilliseconds = 500;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Gera a página. <paramref name="generatedAt"/> sai no rodapé como veio (o chamador passa a hora local). <paramref name="translation"/> dá a contagem de linhas do projeto de um jogo; sem ela, <see cref="ReadTranslation"/> lê o projeto no disco.</summary>
    public static LibraryCatalogResult Render(
        GameLibrary library,
        PlayLog played,
        DateTimeOffset generatedAt,
        LibraryCatalogOptions? options = null,
        ILibrarySheetSource? sheets = null,
        Func<LibraryGame, StatusCounts?>? translation = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(played);
        options ??= new LibraryCatalogOptions(library.Privacy);
        translation ??= ReadTranslation;

        var games = library.Sorted().Where(g => g.Info is not null).ToList();
        var skipped = library.Games.Count - games.Count;
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n");
        html.Append("<html lang=\"pt-BR\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\" />\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        html.Append("<meta name=\"referrer\" content=\"no-referrer\" />\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src https://vndb.org https://*.vndb.org; style-src 'unsafe-inline'\" />\n");
        html.Append("<title>Catálogo de VNs</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n");
        html.Append("<header>\n<h1>Catálogo de VNs</h1>\n");
        html.Append("<p class=\"count\">").Append(games.Count == 1 ? "1 VN" : $"{games.Count} VNs").Append("</p>\n</header>\n<main>\n");

        if (games.Count == 0)
        {
            html.Append("<p class=\"empty\">Nenhuma VN com jogo do VNDB confirmado ainda.</p>\n");
        }

        foreach (var game in games)
        {
            AppendGame(html, game, options, sheets?.SheetOf(game), played.StatsOf(game.Id), translation(game));
        }

        html.Append("</main>\n");
        AppendFooter(html, generatedAt);
        html.Append("</body>\n</html>\n");
        return new LibraryCatalogResult(html.ToString(), games.Count, skipped);
    }

    /// <summary>
    /// O andamento do projeto ligado ao jogo (<see cref="LibraryGame.ProjectPath"/>): só lê <c>vnforge.json</c> e <c>text/*.json</c>. Nulo quando o jogo não tem projeto ou a pasta não se lê.
    /// </summary>
    public static StatusCounts? ReadTranslation(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.ProjectPath is null)
        {
            return null;
        }

        try
        {
            return ProjectStatusReporter.Create(ProjectFolder.Open(game.ProjectPath)).Total;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Grava a página sem deixar o arquivo pela metade (escreve ao lado e troca), criando a pasta se preciso.</summary>
    public static void Write(string path, string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(html);
        AtomicFile.WriteAllText(path, html);
    }

    private static void AppendGame(StringBuilder html, LibraryGame game, LibraryCatalogOptions options, LibrarySheet? sheet, PlayStats stats, StatusCounts? counts)
    {
        var privacy = options.Privacy;
        var card = LibraryVisibility.Card(game, privacy)!;
        html.Append("<article class=\"game\" id=\"").Append(E(game.Id)).Append("\">\n");

        html.Append("<div class=\"cover\">");
        if (card.CoverUrl is { } cover && VndbImageVisibility.IsAllowedSource(cover))
        {
            AppendImage(html, cover, "Capa de " + card.Title, VndbLink(game));
        }
        else if (card.CoverHidden)
        {
            html.Append("<span class=\"hidden\">Capa escondida (conteúdo adulto)</span>");
        }

        html.Append("</div>\n<div class=\"body\">\n");
        html.Append("<h2>").Append(E(card.Title)).Append("</h2>\n");
        if (!string.IsNullOrWhiteSpace(card.OriginalTitle) && card.OriginalTitle != card.Title)
        {
            html.Append("<p class=\"original\">").Append(E(card.OriginalTitle)).Append("</p>\n");
        }

        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(card.Released))
        {
            facts.Add("Lançamento: " + E(card.Released));
        }

        if (card.Length is not null || card.LengthMinutes is not null)
        {
            facts.Add("Duração: " + E(LibraryLength.Text(card.LengthMinutes, card.Length)));
        }

        if (VndbRating.Label(game.Info!.Rating) is { } rating)
        {
            facts.Add(E(rating) + " no VNDB");
        }

        if (VndbLink(game) is { } link)
        {
            facts.Add($"<a href=\"{E(link)}\">Ficha no VNDB</a>");
        }

        AppendLine(html, "facts", facts);
        AppendMine(html, game, options, stats, counts);
        AppendSynopsis(html, game, privacy, sheet);
        AppendTags(html, card, sheet);
        AppendCharacters(html, privacy, sheet);
        html.Append("</div>\n</article>\n");
    }

    private static void AppendMine(StringBuilder html, LibraryGame game, LibraryCatalogOptions options, PlayStats stats, StatusCounts? counts)
    {
        var mine = new List<string>();
        if (game.CurrentList is { } state)
        {
            var fields = state.Fields;
            if (fields.Vote is { } vote)
            {
                mine.Add("Sua nota: " + E(LibraryListRules.FormatVote(vote, PortugueseBrazil)));
            }

            if (fields.Labels.Count > 0)
            {
                mine.Add(E(string.Join(", ", fields.Labels.Select(LibraryListLabels.Display))));
            }

            if (fields.Started is { } started)
            {
                mine.Add("começou em " + Iso(started));
            }

            if (fields.Finished is { } finished)
            {
                mine.Add("terminou em " + Iso(finished));
            }
        }

        AppendLine(html, "mine", mine);

        if (stats.Sessions > 0)
        {
            var sessions = stats.Sessions == 1 ? "1 sessão" : $"{stats.Sessions} sessões";
            var last = stats.LastPlayedAt is { } at ? $", a última em {at.ToLocalTime().ToString("d", PortugueseBrazil)}" : string.Empty;
            AppendLine(html, "played", [$"Jogado: {E(PlayTime.Format(stats.Total))} em {sessions}{E(last)}"]);
        }

        if (counts is { Total: > 0 })
        {
            var ready = Math.Floor(1000.0 * counts.Ready / counts.Total) / 10;
            AppendLine(html, "translated", [$"Tradução para o português: {Percent(counts.Percent)} das linhas, {Percent(ready)} revisadas"]);
        }

        if (options.IncludeNotes && game.CurrentList?.Fields.Notes is { Length: > 0 } notes)
        {
            html.Append("<p class=\"notes\">").Append(E(notes)).Append("</p>\n");
        }
    }

    private static void AppendSynopsis(StringBuilder html, LibraryGame game, LibraryPrivacy privacy, LibrarySheet? sheet)
    {
        var translated = sheet?.Synopsis is not null && sheet.SynopsisTranslated;
        var source = sheet?.Synopsis ?? game.Info!.Synopsis;
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var text = LibraryVisibility.PlainSynopsis(source, privacy.ShowSpoilers, out _);
        if (text.Length == 0)
        {
            return;
        }

        html.Append("<section class=\"synopsis\">\n<h3>Sinopse");
        if (!translated)
        {
            html.Append(" <small>(texto original do VNDB)</small>");
        }

        html.Append("</h3>\n");
        AppendParagraphs(html, text);
        html.Append("</section>\n");
    }

    private static void AppendTags(StringBuilder html, LibraryGameCard card, LibrarySheet? sheet)
    {
        if (card.Tags.Count == 0)
        {
            return;
        }

        html.Append("<ul class=\"tags\">");
        foreach (var tag in card.Tags)
        {
            var name = sheet?.TagNames.TryGetValue(tag.Id, out var translated) == true && !string.IsNullOrWhiteSpace(translated) ? translated : tag.Name;
            html.Append("<li>").Append(E(name)).Append("</li>");
        }

        html.Append("</ul>\n");
    }

    private static void AppendCharacters(StringBuilder html, LibraryPrivacy privacy, LibrarySheet? sheet)
    {
        var characters = sheet?.Characters.Where(c => privacy.ShowSpoilers || c.Spoiler == 0).ToList() ?? [];
        if (characters.Count == 0)
        {
            return;
        }

        html.Append("<section class=\"characters\">\n<h3>Personagens</h3>\n<ul>\n");
        foreach (var character in characters)
        {
            html.Append("<li>");
            if (character.Image is { } image && VndbImageVisibility.IsAllowedSource(image.Url) && !VndbImageVisibility.IsHidden(image, privacy))
            {
                AppendImage(html, image.Url, "Retrato de " + character.Name, null);
            }

            html.Append("<div><strong>").Append(E(character.Name)).Append("</strong>");
            if (!string.IsNullOrWhiteSpace(character.OriginalName) && character.OriginalName != character.Name)
            {
                html.Append(" <span class=\"original\">").Append(E(character.OriginalName)).Append("</span>");
            }

            if (!string.IsNullOrWhiteSpace(character.Role))
            {
                html.Append(" <em>").Append(E(character.Role)).Append("</em>");
            }

            var traits = character.Traits.Where(t => privacy.ShowSpoilers || t.Spoiler == 0).Select(t => t.Name).ToList();
            if (traits.Count > 0)
            {
                html.Append("<br />").Append(E(string.Join(", ", traits)));
            }

            if (!string.IsNullOrWhiteSpace(character.Description))
            {
                var description = LibraryVisibility.PlainSynopsis(character.Description, privacy.ShowSpoilers, out _);
                if (description.Length > 0)
                {
                    html.Append("<br />").Append(E(description.Replace('\n', ' ').Replace('\r', ' ')));
                }
            }

            html.Append("</div></li>\n");
        }

        html.Append("</ul>\n</section>\n");
    }

    private static void AppendFooter(StringBuilder html, DateTimeOffset generatedAt)
    {
        html.Append("<footer>\n");
        html.Append($"<p>Dados das fichas: <a href=\"{VndbHome}\">VNDB</a> (vndb.org), usados sob a <a href=\"{OdblUrl}\">Open Database License</a> (ODbL 1.0). ");
        html.Append($"As imagens são links para o VNDB, não cópias, e continuam sob os direitos de quem as criou (<a href=\"{DatabaseLicenseUrl}\">licença dos dados do VNDB</a>).</p>\n");
        html.Append("<p>Gerado pelo VNForge em ").Append(generatedAt.ToString("d", PortugueseBrazil)).Append(" às ").Append(generatedAt.ToString("HH:mm", PortugueseBrazil)).Append(".</p>\n");
        html.Append("</footer>\n");
    }

    private static void AppendImage(StringBuilder html, string url, string alt, string? link)
    {
        var image = $"<img src=\"{E(url)}\" alt=\"{E(alt)}\" loading=\"lazy\" />";
        html.Append(link is null ? image : $"<a href=\"{E(link)}\">{image}</a>");
    }

    private static void AppendLine(StringBuilder html, string cssClass, List<string> parts)
    {
        if (parts.Count > 0)
        {
            html.Append("<p class=\"").Append(cssClass).Append("\">").Append(string.Join(" · ", parts)).Append("</p>\n");
        }
    }

    private static void AppendParagraphs(StringBuilder html, string text)
    {
        foreach (var paragraph in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            html.Append("<p>").Append(E(paragraph)).Append("</p>\n");
        }
    }

    private static string? VndbLink(LibraryGame game) =>
        game.VndbId is { } id && VndbId().IsMatch(id) ? VndbHome + id : null;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Percent(double value) => value.ToString("0.#", PortugueseBrazil) + "%";

    /// <summary>Escapa para texto e atributo, e tira os caracteres de controle que um documento XML não aceita.</summary>
    private static string E(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                case < ' ' when c is not ('\t' or '\n' or '\r'): break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\Av\d{1,9}\z", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex VndbId();

    private const string Style = """

        :root { color-scheme: light dark; --fg: #1d1d1f; --bg: #f6f6f7; --card: #ffffff; --muted: #5c5c63; --line: #d9d9de; --accent: #1a5fb4; }
        @media (prefers-color-scheme: dark) { :root { --fg: #ececf0; --bg: #17171a; --card: #202024; --muted: #a0a0aa; --line: #34343a; --accent: #78aeed; } }
        body { margin: 0; font: 16px/1.5 system-ui, sans-serif; color: var(--fg); background: var(--bg); }
        header, main, footer { max-width: 62rem; margin: 0 auto; padding: 0 1rem; }
        header { padding-top: 1.5rem; }
        h1 { margin: 0; }
        .count { color: var(--muted); margin: .25rem 0 1rem; }
        a { color: var(--accent); }
        .game { display: flex; gap: 1rem; background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 1rem; margin: 1rem 0; }
        .cover { flex: 0 0 9rem; }
        .cover img { width: 9rem; height: auto; border-radius: 4px; display: block; }
        .hidden { color: var(--muted); font-size: .85rem; }
        .body { flex: 1; min-width: 0; }
        h2 { margin: 0; font-size: 1.3rem; }
        h3 { margin: 1rem 0 .25rem; font-size: 1rem; }
        h3 small { font-weight: normal; color: var(--muted); }
        .original { color: var(--muted); margin: 0; }
        .facts, .mine, .played, .translated { margin: .35rem 0 0; }
        .facts { color: var(--muted); }
        .notes { margin: .5rem 0 0; padding-left: .75rem; border-left: 3px solid var(--line); white-space: pre-wrap; }
        .tags { list-style: none; display: flex; flex-wrap: wrap; gap: .35rem; padding: 0; margin: .75rem 0 0; }
        .tags li { border: 1px solid var(--line); border-radius: 999px; padding: 0 .6rem; font-size: .85rem; }
        .characters ul { list-style: none; padding: 0; margin: 0; display: grid; gap: .6rem; }
        .characters li { display: flex; gap: .6rem; font-size: .9rem; }
        .characters img { width: 3.5rem; height: auto; align-self: flex-start; border-radius: 4px; }
        footer { color: var(--muted); font-size: .85rem; padding-bottom: 2rem; }
        @media (max-width: 36rem) { .game { flex-direction: column; } }
        """;
}
