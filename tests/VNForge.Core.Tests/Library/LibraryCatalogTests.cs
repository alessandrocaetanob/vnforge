using System.Xml;
using System.Xml.Linq;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Library;

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
    public void A_pagina_traz_a_ficha_o_que_voce_jogou_a_sua_nota_e_o_que_traduziu()
    {
        var game = Clannad();

        var result = Render([game], played: Played(game), translation: _ => Counts(untranslated: 25, machine: 25, approved: 50));

        var html = result.Html;
        Assert.Contains("CLANNAD", html, StringComparison.Ordinal);
        Assert.Contains("クラナド", html, StringComparison.Ordinal);
        Assert.Contains("2004-04-28", html, StringComparison.Ordinal);
        Assert.Contains("nota 8,6", html, StringComparison.Ordinal);
        Assert.Contains("Sua nota: 8,5", html, StringComparison.Ordinal);
        Assert.Contains("Terminado", html, StringComparison.Ordinal);
        Assert.Contains("3 h 05 min", html, StringComparison.Ordinal);
        Assert.Contains("2 sessões", html, StringComparison.Ordinal);
        Assert.Contains("75%", html, StringComparison.Ordinal);
        Assert.Contains("2026-03-04", html, StringComparison.Ordinal);
        Assert.Contains("cerca de 60 h", html, StringComparison.Ordinal);
        Assert.Equal(1, result.Games);
    }

    [Fact]
    public void Cada_parte_desligada_some_da_pagina_e_o_credito_ao_VNDB_fica()
    {
        var game = Clannad();
        var options = new LibraryCatalogOptions(new LibraryPrivacy(), IncludeSheet: false, IncludePlayed: false, IncludeVote: false, IncludeTranslation: false);

        var html = Render([game], options, played: Played(game), translation: _ => Counts(untranslated: 25, machine: 25, approved: 50)).Html;

        Assert.Contains("CLANNAD", html, StringComparison.Ordinal);
        Assert.Contains("Open Database License", html, StringComparison.Ordinal);
        foreach (var gone in new[] { "Sinopse", "Uma história de família", "Drama", "Sua nota", "Terminado", "Jogado", "2026-03-04", "Tradução para o português" })
        {
            Assert.DoesNotContain(gone, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Jogo_sem_tempo_de_jogo_nem_projeto_nao_inventa_linhas()
    {
        var result = Render([Adult()], new LibraryCatalogOptions(new LibraryPrivacy(Adult: AdultMode.Show)));

        Assert.DoesNotContain("Jogado", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Tradução", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sua nota", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void O_rodape_credita_o_VNDB_e_a_ODbL()
    {
        var html = Render([Clannad()]).Html;

        Assert.Contains("https://vndb.org/", html, StringComparison.Ordinal);
        Assert.Contains("Open Database License", html, StringComparison.Ordinal);
        Assert.Contains("https://opendatacommons.org/licenses/odbl/1-0/", html, StringComparison.Ordinal);
        Assert.Contains("https://vndb.org/d17", html, StringComparison.Ordinal);
        Assert.Contains("<footer", html, StringComparison.Ordinal);
    }

    [Fact]
    public void As_imagens_entram_como_link_para_o_VNDB_e_nenhuma_e_copiada()
    {
        var html = Render([Clannad()]).Html;

        Assert.Contains($"src=\"{CoverUrl}\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://vndb.org/v4\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("base64", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        var sources = Parse(html).Descendants().Where(e => e.Name.LocalName == "img").Select(e => (string?)e.Attribute("src")).ToList();
        Assert.NotEmpty(sources);
        Assert.All(sources, src => Assert.True(VndbImageVisibility.IsAllowedSource(src), src));
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
    public void Spoiler_e_tag_erotica_ficam_escondidos_por_padrao()
    {
        var html = Render([Clannad()]).Html;

        Assert.Contains("Drama", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Ela morre no fim", html, StringComparison.Ordinal);
        Assert.Contains(LibraryVisibility.SpoilerPlaceholder, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Final triste", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Cena de sexo", html, StringComparison.Ordinal);
        Assert.Contains("Fim da sinopse.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Com_a_privacidade_liberada_spoiler_e_tags_aparecem()
    {
        var html = Render([Clannad()], new LibraryCatalogOptions(new LibraryPrivacy(ShowSpoilers: true, Adult: AdultMode.Show))).Html;

        Assert.Contains("Ela morre no fim", html, StringComparison.Ordinal);
        Assert.Contains("Final triste", html, StringComparison.Ordinal);
        Assert.Contains("Cena de sexo", html, StringComparison.Ordinal);
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
    public void As_suas_notas_so_entram_quando_pedidas()
    {
        var without = Render([Clannad()]).Html;
        var with = Render([Clannad()], new LibraryCatalogOptions(new LibraryPrivacy(), IncludeNotes: true)).Html;

        Assert.DoesNotContain("Chorei no terceiro ato.", without, StringComparison.Ordinal);
        Assert.Contains("Chorei no terceiro ato.", with, StringComparison.Ordinal);
    }

    [Fact]
    public void Jogo_sem_jogo_do_VNDB_confirmado_fica_de_fora_e_e_contado()
    {
        var result = Render([Clannad(), Unmatched()]);

        Assert.Equal(1, result.Games);
        Assert.Equal(1, result.Skipped);
        Assert.DoesNotContain("Sem casamento", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Biblioteca_vazia_gera_uma_pagina_valida_com_o_aviso()
    {
        var result = Render([]);

        Assert.Equal(0, result.Games);
        Assert.Contains("Nenhuma VN", result.Html, StringComparison.Ordinal);
        _ = Parse(result.Html);
    }

    [Fact]
    public void Os_jogos_saem_em_ordem_de_titulo()
    {
        var zed = Clannad() with { Id = "gz", Title = "Zed", Info = Clannad().Info! with { Title = "Zed" } };
        var alpha = Clannad() with { Id = "ga", Title = "Alfa", Info = Clannad().Info! with { Title = "Alfa" } };

        var html = Render([zed, alpha]).Html;

        Assert.True(html.IndexOf("Alfa", StringComparison.Ordinal) < html.IndexOf("Zed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ficha_traduzida_substitui_o_texto_original_e_sem_ela_vale_o_do_VNDB()
    {
        var sheets = new FakeSheets(new LibrarySheet(
            "Uma história de família, em português.",
            true,
            [new LibrarySheetCharacter("Nagisa Furukawa", "古河 渚", "Protagonista", "Uma garota frágil.", true, [new LibrarySheetTrait("Tímida", 0, false, false), new LibrarySheetTrait("Morre", 2, false, false)], new VndbImageRef("https://t.vndb.org/ch/12/3456.jpg"), 0)],
            new Dictionary<string, string> { ["g1"] = "Drama (pt)" }));

        var translated = Render([Clannad()], sheets: sheets).Html;
        var original = Render([Clannad()]).Html;

        Assert.Contains("Uma história de família, em português.", translated, StringComparison.Ordinal);
        Assert.DoesNotContain("Fim da sinopse", translated, StringComparison.Ordinal);
        Assert.Contains("Nagisa Furukawa", translated, StringComparison.Ordinal);
        Assert.Contains("Protagonista", translated, StringComparison.Ordinal);
        Assert.Contains("Tímida", translated, StringComparison.Ordinal);
        Assert.DoesNotContain("Morre", translated, StringComparison.Ordinal);
        Assert.Contains("https://t.vndb.org/ch/12/3456.jpg", translated, StringComparison.Ordinal);
        Assert.Contains("Drama (pt)", translated, StringComparison.Ordinal);
        Assert.Contains("Fim da sinopse", original, StringComparison.Ordinal);
        Assert.DoesNotContain("Personagens", original, StringComparison.Ordinal);
    }

    [Fact]
    public void Personagem_com_spoiler_e_retrato_adulto_seguem_a_privacidade()
    {
        var sheets = new FakeSheets(new LibrarySheet(
            null,
            false,
            [
                new LibrarySheetCharacter("Segredo", null, null, "Vilã do final.", false, [], null, 2),
                new LibrarySheetCharacter("Nua", null, null, null, false, [], new VndbImageRef("https://t.vndb.org/ch/00/0001.jpg", Sexual: 2), 0),
            ],
            new Dictionary<string, string>()));

        var hidden = Render([Clannad()], sheets: sheets).Html;
        var shown = Render([Clannad()], new LibraryCatalogOptions(new LibraryPrivacy(true, AdultMode.Show)), sheets).Html;

        Assert.DoesNotContain("Segredo", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("0001.jpg", hidden, StringComparison.Ordinal);
        Assert.Contains("Segredo", shown, StringComparison.Ordinal);
        Assert.Contains("0001.jpg", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void Traco_sexual_e_o_sem_marca_somem_sem_o_conteudo_adulto_e_a_mentira_sem_os_spoilers()
    {
        var sheets = new FakeSheets(new LibrarySheet(
            null,
            false,
            [
                new LibrarySheetCharacter(
                    "Ana",
                    null,
                    null,
                    null,
                    false,
                    [
                        new LibrarySheetTrait("Tímida", 0, false, false),
                        new LibrarySheetTrait("Traço picante", 0, false, true),
                        new LibrarySheetTrait("Traço de ficha antiga", 0, false, null),
                        new LibrarySheetTrait("Traço enganoso", 0, true, false),
                    ],
                    null,
                    0),
            ],
            new Dictionary<string, string>()));

        var hidden = Render([Clannad()], sheets: sheets).Html;
        var adult = Render([Clannad()], new LibraryCatalogOptions(new LibraryPrivacy(false, AdultMode.Show)), sheets).Html;
        var spoilers = Render([Clannad()], new LibraryCatalogOptions(new LibraryPrivacy(true, AdultMode.Blur)), sheets).Html;

        Assert.Contains("Tímida", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço picante", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço de ficha antiga", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço enganoso", hidden, StringComparison.Ordinal);
        Assert.Contains("Traço picante", adult, StringComparison.Ordinal);
        Assert.Contains("Traço de ficha antiga", adult, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço enganoso", adult, StringComparison.Ordinal);
        Assert.Contains("Traço enganoso", spoilers, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço picante", spoilers, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ultima_sessao_usa_o_fuso_da_hora_de_geracao_e_nao_o_da_maquina()
    {
        var game = Clannad();
        var log = new PlayLog();
        var end = new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.FromHours(-3));
        log.Upsert(game.Id, new PlaySession("s1", end.AddHours(-1), end, TimeSpan.FromHours(1)));
        var libraryOf = new GameLibrary([game]);

        var inBrasilia = LibraryCatalog.Render(libraryOf, log, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(-3)), null, null, _ => null).Html;
        var inUtc = LibraryCatalog.Render(libraryOf, log, new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), null, null, _ => null).Html;

        Assert.Contains("a última em 09/11/2026", inBrasilia, StringComparison.Ordinal);
        Assert.Contains("a última em 10/11/2026", inUtc, StringComparison.Ordinal);
    }

    [Fact]
    public void Texto_com_caractere_que_o_XML_nao_aceita_continua_gerando_documento_valido()
    {
        var game = Clannad();
        var broken = game with
        {
            Info = game.Info! with { Title = "Ruim\uD800\uFFFE\uFFFF\u0001Fim \uD83D\uDE00 par", Synopsis = "Sozinho\uDC00 depois" },
        };

        var html = Render([broken]).Html;

        _ = Parse(html);
        Assert.Contains("RuimFim \uD83D\uDE00 par", html, StringComparison.Ordinal);
        Assert.Contains("Sozinho depois", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Destino_dentro_da_pasta_de_um_jogo_e_reconhecido()
    {
        var root = Path.Combine(Path.GetTempPath(), "vnforge-catalog-" + Guid.NewGuid().ToString("N"));
        var gameFolder = Path.Combine(root, "CLANNAD");
        var other = Path.Combine(root, "CLANNAD 2");
        Directory.CreateDirectory(gameFolder);
        Directory.CreateDirectory(other);
        try
        {
            var game = Clannad() with { Folder = gameFolder };
            var library = new GameLibrary([game]);

            Assert.Same(game, LibraryCatalog.GameHolding(library, Path.Combine(gameFolder, "catalogo.html")));
            Assert.Same(game, LibraryCatalog.GameHolding(library, Path.Combine(gameFolder, "novo", "catalogo.html")));
            Assert.Same(game, LibraryCatalog.GameHolding(library, Path.Combine(gameFolder, "novo", "..", "catalogo.html")));
            Assert.Null(LibraryCatalog.GameHolding(library, Path.Combine(other, "catalogo.html")));
            Assert.Null(LibraryCatalog.GameHolding(library, Path.Combine(root, "catalogo.html")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    [Fact]
    public void A_data_de_geracao_aparece_no_rodape_em_pt_BR()
    {
        var html = Render([Clannad()]).Html;

        Assert.Contains("09/11/2026", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Projeto_que_nao_se_le_ou_ausente_nao_tem_andamento()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vnforge-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var game = Clannad() with { ProjectPath = folder };

            Assert.Null(LibraryCatalog.ReadTranslation(game));
            Assert.Null(LibraryCatalog.ReadTranslation(game with { ProjectPath = null }));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"version\": 1, \"units\": [{\"id\": \"a:1\", \"text\"")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"version\": 1, \"units\": [null]}")]
    [InlineData("{\"version\": 1, \"units\": {\"a\": 1}}")]
    [InlineData("{\"version\": 1, \"version\": 2, \"units\": []}")]
    public void Projeto_com_texto_truncado_ou_malformado_nao_derruba_o_catalogo(string textJson)
    {
        var folder = Path.Combine(Path.GetTempPath(), "vnforge-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "text"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "vnforge.json"), "{\"version\": 1");
            File.WriteAllText(Path.Combine(folder, "text", "script.json"), textJson);
            var game = Clannad() with { ProjectPath = folder };

            Assert.Null(LibraryCatalog.ReadTranslation(game));
            Assert.Equal(1, LibraryCatalog.Render(new GameLibrary([game]), new PlayLog(), Generated).Games);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Projeto_com_texto_fora_de_UTF8_nao_derruba_o_catalogo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vnforge-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "text"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "vnforge.json"), "{}");
            File.WriteAllBytes(Path.Combine(folder, "text", "script.json"), [0x7B, 0xFF, 0xFE, 0x7D]);

            Assert.Null(LibraryCatalog.ReadTranslation(Clannad() with { ProjectPath = folder }));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class FakeSheets(LibrarySheet sheet) : ILibrarySheetSource
    {
        public LibrarySheet? SheetOf(LibraryGame game) => sheet;
    }
}
