using System.Xml;
using System.Xml.Linq;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R9")]
public sealed class LibraryCatalogTests
{
    private const string CoverUrl = "https://t.vndb.org/cv/34/1234.jpg";

    private static readonly DateTimeOffset Generated = new(2026, 11, 9, 15, 30, 0, TimeSpan.Zero);

    private static LibraryGame Clannad() =>
        TagTestGames.Plain("CLANNAD") with
        {
            VndbId = "v4",
            Confirmation = LibraryConfirmation.Manual,
            ProjectPath = "C:\\Projetos\\clannad",
            Info = new LibraryVndbInfo(
                "CLANNAD",
                "クラナド",
                "2004-04-28",
                CoverUrl,
                "Uma história de família. [spoiler]Ela morre no fim.[/spoiler] Fim da sinopse.",
                5,
                3600,
                120,
                [
                    new LibraryTag("g1", "Drama", "cont", 3, 0),
                    new LibraryTag("g2", "Final triste", "cont", 2.5, 2),
                    new LibraryTag("g3", "Cena de sexo", "ero", 2, 0),
                ],
                false,
                86.4),
            List = new LibraryListState(
                "v4",
                new LibraryListFields
                {
                    Labels = [LibraryListLabels.Named(LibraryListLabels.Finished)],
                    Vote = 85,
                    Started = new DateOnly(2026, 1, 5),
                    Finished = new DateOnly(2026, 3, 4),
                    Notes = "Chorei no terceiro ato.",
                },
                Generated),
        };

    private static LibraryGame Adult() =>
        TagTestGames.Plain("Adulto") with
        {
            VndbId = "v9",
            Confirmation = LibraryConfirmation.Auto,
            Info = new LibraryVndbInfo("Jogo Adulto", null, "2010", "https://t.vndb.org/cv/99/9999.jpg", null, null, null, 0, [], true),
        };

    private static LibraryGame Unmatched() => TagTestGames.Plain("Sem casamento");

    private static PlayLog Played(LibraryGame game)
    {
        var log = new PlayLog();
        log.Upsert(game.Id, new PlaySession("s1", Generated.AddDays(-3), Generated.AddDays(-3).AddHours(2), TimeSpan.FromMinutes(125)));
        log.Upsert(game.Id, new PlaySession("s2", Generated.AddDays(-1), Generated.AddDays(-1).AddHours(1), TimeSpan.FromMinutes(60)));
        return log;
    }

    private static LibraryCatalogResult Render(
        IEnumerable<LibraryGame> games,
        LibraryCatalogOptions? options = null,
        ILibrarySheetSource? sheets = null,
        PlayLog? played = null,
        Func<LibraryGame, StatusCounts?>? translation = null) =>
        LibraryCatalog.Render(
            new GameLibrary(games),
            played ?? new PlayLog(),
            Generated,
            options,
            sheets,
            translation ?? (_ => null));

    private static StatusCounts Counts(int untranslated, int machine, int approved) => new(untranslated, machine, 0, 0, approved);

    private static XDocument Parse(string html)
    {
        using var reader = XmlReader.Create(new StringReader(html), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return XDocument.Load(reader);
    }

    [Fact]
    public void Endereco_de_imagem_que_nao_e_do_VNDB_nao_vira_img()
    {
        var game = Clannad();
        var hostile = game with { Info = game.Info! with { CoverUrl = "http://evil.example/cover.jpg" } };

        var html = Render([hostile]).Html;

        Assert.DoesNotContain("evil.example", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void O_jogo_adulto_nem_entra_na_pagina_ate_a_privacidade_liberar()
    {
        var hidden = Render([Clannad(), Adult()]);
        var shown = Render([Clannad(), Adult()], new LibraryCatalogOptions(new LibraryPrivacy(Adult: AdultMode.Show)));

        Assert.Equal(1, hidden.Games);
        Assert.Equal(1, hidden.Adult);
        Assert.DoesNotContain("Jogo Adulto", hidden.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("9999.jpg", hidden.Html, StringComparison.Ordinal);
        Assert.Equal(2, shown.Games);
        Assert.Equal(0, shown.Adult);
        Assert.Contains("9999.jpg", shown.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Todo_texto_e_escapado_e_a_pagina_nao_executa_nada()
    {
        var game = Clannad();
        var hostile = game with
        {
            Info = game.Info! with
            {
                Title = "<img src=x onerror=alert(1)>\"&'",
                Synopsis = "<script>alert(2)</script> texto",
                Tags = [new LibraryTag("g9", "<b>negrito</b>", "cont", 3, 0)],
            },
            List = game.List! with { Fields = game.List.Fields with { Notes = "<script>alert(3)</script>" } },
        };

        var html = Render([hostile], new LibraryCatalogOptions(new LibraryPrivacy(), IncludeNotes: true)).Html;

        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>negrito", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(2)&lt;/script&gt; texto", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        _ = Parse(html);
    }

    [Fact]
    public void A_pagina_e_um_documento_bem_formado_e_abre_sem_erro()
    {
        var game = Clannad();
        var sheets = new FakeSheets(new LibrarySheet(
            "Sinopse & \"aspas\"",
            true,
            [new LibrarySheetCharacter("Ana", null, "Principal", "Descrição", true, [new LibrarySheetTrait("Alta", 0, false, false)], new VndbImageRef("https://t.vndb.org/ch/12/3456.jpg"), 0)],
            new Dictionary<string, string>()));

        var html = Render([game, Adult(), Unmatched()], new LibraryCatalogOptions(new LibraryPrivacy(true, AdultMode.Show), true), sheets, Played(game), _ => Counts(1, 1, 2)).Html;

        var document = Parse(html);
        Assert.Equal("html", document.Root!.Name.LocalName);
        Assert.Equal("pt-BR", (string?)document.Root.Attribute("lang"));
        Assert.Equal(2, document.Descendants().Count(e => e.Name.LocalName == "article"));
        Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("charset=\"utf-8\"", html, StringComparison.Ordinal);
    }

    private sealed class FakeSheets(LibrarySheet sheet) : ILibrarySheetSource
    {
        public LibrarySheet? SheetOf(LibraryGame game) => sheet;
    }
}
