using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VNForge.Core.Factsheet;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>
/// Como gerar o catálogo. <paramref name="Privacy"/> decide o que sai escondido (spoiler e conteúdo adulto, como na Biblioteca); sem <c>ShowAdult</c>, o jogo marcado como adulto nem entra na página. <paramref name="IncludeNotes"/> põe as notas de
/// texto que a pessoa escreveu em cada jogo; ficam de fora por padrão, porque o catálogo é feito para ser mostrado. Os outros quatro escolhem o que entra de cada jogo (o diálogo do app):
/// <paramref name="IncludeSheet"/> é a sinopse, as tags e os personagens, <paramref name="IncludePlayed"/> o estado da lista, as datas e o tempo jogado, <paramref name="IncludeVote"/> a nota e
/// <paramref name="IncludeTranslation"/> o andamento do projeto de tradução. Entram por padrão; título, capa e o crédito ao VNDB entram sempre.
/// </summary>
public sealed record LibraryCatalogOptions(
    LibraryPrivacy Privacy,
    bool IncludeNotes = false,
    bool IncludeSheet = true,
    bool IncludePlayed = true,
    bool IncludeVote = true,
    bool IncludeTranslation = true);

/// <summary>A página gerada. <paramref name="Games"/> são os jogos que entraram; <paramref name="Skipped"/> são os que ficaram de fora por ainda não terem jogo do VNDB confirmado; <paramref name="Adult"/> são os de conteúdo adulto que a privacidade deixou de fora.</summary>
public sealed record LibraryCatalogResult(string Html, int Games, int Skipped, int Adult = 0);

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

    private static readonly string[] CategoryNames = ["Muito curta", "Curta", "Média", "Longa", "Muito longa"];

    /// <summary>
    /// A duração como a grade e a gaveta a escrevem: "Média · 18 h", a faixa do VNDB e as horas votadas. Sem a faixa, ela sai dos minutos; sem as horas, só a faixa;
    /// nulo quando o VNDB não tem nenhum dos dois.
    /// </summary>
    public static string? Short(int? minutes, int? category)
    {
        var votes = minutes is > 0 ? minutes : null;
        var band = category is >= 1 and <= 5 ? category : votes is { } m ? CategoryOf(m) : null;
        if (band is null)
        {
            return null;
        }

        var name = CategoryNames[band.Value - 1];
        return votes is { } minutesVoted ? $"{name} · {Hours(minutesVoted)}" : name;
    }

    private static int CategoryOf(int minutes) =>
        minutes < 120 ? 1 : minutes < 600 ? 2 : minutes < 1800 ? 3 : minutes < 3000 ? 4 : 5;

    private static string Hours(int minutes) =>
        minutes < 60 ? $"{minutes} min" : $"{((int)Math.Round(minutes / 60.0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)} h";
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
    /// <summary>O título da página; o app o usa para reconhecer um catálogo antigo antes de trocá-lo.</summary>
    public const string PageTitle = "Catálogo de VNs";

    private const string VndbHome = "https://vndb.org/";
    private const string OdblUrl = "https://opendatacommons.org/licenses/odbl/1-0/";
    private const string DatabaseLicenseUrl = "https://vndb.org/d17";
    private const int RegexTimeoutMilliseconds = 500;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Gera a página. <paramref name="generatedAt"/> sai no rodapé como veio (o chamador passa a hora local) e o seu deslocamento vale também para a data da última sessão, para a página não mudar com o fuso da máquina. <paramref name="translation"/> dá a contagem de linhas do projeto de um jogo; sem ela, <see cref="ReadTranslation"/> lê o projeto no disco.</summary>
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

        var matched = library.Sorted().Where(g => g.Info is not null).ToList();
        var games = matched.Where(g => options.Privacy.ShowAdult || !g.Info!.Adult).ToList();
        var skipped = library.Games.Count - matched.Count;
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n");
        html.Append("<html lang=\"pt-BR\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\" />\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        html.Append("<meta name=\"referrer\" content=\"no-referrer\" />\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src https://vndb.org https://*.vndb.org; style-src 'unsafe-inline'\" />\n");
        html.Append("<title>").Append(PageTitle).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n");
        html.Append("<header>\n<h1>").Append(PageTitle).Append("</h1>\n");
        html.Append("<p class=\"count\">").Append(games.Count == 1 ? "1 VN" : $"{games.Count} VNs").Append("</p>\n</header>\n<main>\n");

        if (games.Count == 0)
        {
            html.Append("<p class=\"empty\">Nenhuma VN com jogo do VNDB confirmado ainda.</p>\n");
        }

        foreach (var game in games)
        {
            AppendGame(html, game, options, sheets?.SheetOf(game), played.StatsOf(game.Id), translation(game), generatedAt.Offset);
        }

        html.Append("</main>\n");
        AppendFooter(html, generatedAt);
        html.Append("</body>\n</html>\n");
        return new LibraryCatalogResult(html.ToString(), games.Count, skipped, matched.Count - games.Count);
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

    /// <summary>
    /// O jogo da biblioteca em cuja pasta <paramref name="path"/> cai (a própria pasta ou qualquer coisa dentro dela, seguindo links), ou nulo. O catálogo não grava na pasta de um jogo:
    /// o VNForge só altera essa pasta pelo fluxo de backup.
    /// </summary>
    public static LibraryGame? GameHolding(GameLibrary library, string path)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var target = ProjectExtractor.ResolveLinks(path);
        var typed = Path.GetFullPath(path);
        return library.Games.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Folder)
            && (ProjectExtractor.IsInside(Path.GetFullPath(g.Folder), typed) || ProjectExtractor.IsInside(ProjectExtractor.ResolveLinks(g.Folder), target)));
    }

    private static void AppendGame(StringBuilder html, LibraryGame game, LibraryCatalogOptions options, LibrarySheet? sheet, PlayStats stats, StatusCounts? counts, TimeSpan offset)
    {
        var privacy = options.Privacy;
        var card = LibraryVisibility.Card(game, privacy)!;
        html.Append("<article class=\"game\" id=\"").Append(E(game.Id)).Append("\">\n");

        html.Append("<div class=\"cover\">");
        if (card.CoverUrl is { } cover && VndbImageVisibility.IsAllowedSource(cover))
        {
            AppendImage(html, cover, "Capa de " + card.Title, VndbLink(game));
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
        AppendMine(html, game, options, stats, counts, offset);
        if (options.IncludeSheet)
        {
            AppendSynopsis(html, game, privacy, sheet);
            AppendTags(html, card, sheet);
            AppendCharacters(html, privacy, sheet);
        }

        html.Append("</div>\n</article>\n");
    }

    private static void AppendMine(StringBuilder html, LibraryGame game, LibraryCatalogOptions options, PlayStats stats, StatusCounts? counts, TimeSpan offset)
    {
        var mine = new List<string>();
        if (game.CurrentList is { } state)
        {
            var fields = state.Fields;
            if (options.IncludeVote && fields.Vote is { } vote)
            {
                mine.Add("Sua nota: " + E(LibraryListRules.FormatVote(vote, PortugueseBrazil)));
            }

            if (options.IncludePlayed && fields.Labels.Count > 0)
            {
                mine.Add(E(string.Join(", ", fields.Labels.Select(LibraryListLabels.Display))));
            }

            if (options.IncludePlayed && fields.Started is { } started)
            {
                mine.Add("começou em " + Iso(started));
            }

            if (options.IncludePlayed && fields.Finished is { } finished)
            {
                mine.Add("terminou em " + Iso(finished));
            }
        }

        AppendLine(html, "mine", mine);

        if (options.IncludePlayed && stats.Sessions > 0)
        {
            var sessions = stats.Sessions == 1 ? "1 sessão" : $"{stats.Sessions} sessões";
            var last = stats.LastPlayedAt is { } at ? $", a última em {at.ToOffset(offset).ToString("d", PortugueseBrazil)}" : string.Empty;
            AppendLine(html, "played", [$"Jogado: {E(PlayTime.Format(stats.Total))} em {sessions}{E(last)}"]);
        }

        if (options.IncludeTranslation && counts is { Total: > 0 })
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

            var traits = character.Traits.Where(t => !SheetViewer.IsTraitHidden(t.Spoiler, t.Lie, t.Sexual, privacy)).Select(t => t.Name).ToList();
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

    /// <summary>Escapa para texto e atributo, e tira o que um documento XML não aceita: controles abaixo de espaço, substituto UTF-16 sem par, U+FFFE e U+FFFF.</summary>
    private static string E(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
                continue;
            }

            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                case < ' ' when c is not ('\t' or '\n' or '\r'): break;
                case >= '\uD800' and <= '\uDFFF': break;
                case '\uFFFE' or '\uFFFF': break;
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
