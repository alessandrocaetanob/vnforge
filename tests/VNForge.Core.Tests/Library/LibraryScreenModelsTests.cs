using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Library.TagTestGames;

namespace VNForge.Core.Tests.Library;

/// <summary>Os modelos que a tela da Biblioteca desenha: o cartão da grade, a gaveta, os candidatos do casamento, os filtros e a tabela da primeira varredura.</summary>
public sealed class LibraryScreenModelsTests
{
    private static readonly LibraryPrivacy Hidden = new();
    private static readonly LibraryPrivacy Everything = new(ShowSpoilers: true, Adult: AdultMode.Show);

    private static LibraryGame Yuris(string name) =>
        Plain(name) with { Engine = new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine) };

    private static LibraryGame Signature(string name, string engine = "Siglus") =>
        Plain(name) with { Engine = new LibraryEngine(engine.ToLowerInvariant(), engine, LibraryEngineSource.Signature) };

    private static LibraryGame Matched(LibraryGame game, string cover = "https://t.vndb.org/cv/1.jpg", int? length = 3, int? minutes = 1080, bool adult = false) =>
        game with
        {
            VndbId = "v31201",
            Confirmation = LibraryConfirmation.Auto,
            Info = new LibraryVndbInfo(game.Title, "雨音の箱庭", "2019-04-12", cover, "Numa cidade [spoiler]a caixa mente[/spoiler].", length, minutes, 30,
                [new LibraryTag("g1", "Escola", "cont", 3, 0), new LibraryTag("g2", "Mistério", "cont", 2.8, 0), new LibraryTag("g3", "Chuva", "cont", 2.5, 0), new LibraryTag("g4", "Final triste", "cont", 2, 2), new LibraryTag("g5", "Sexo", "ero", 2, 0), new LibraryTag("g6", "Drama", "cont", 1.5, 0)],
                adult),
        };

    private static StatusCounts Counts(int ready, int total) => new(total - ready, 0, 0, ready, 0);

    [Theory]
    [InlineData(null, 2, "Curta")]
    [InlineData(45, null, "Muito curta · 45 min")]
    [InlineData(360, null, "Curta · 6 h")]
    public void Duracao_curta_junta_a_faixa_e_as_horas(int? minutes, int? category, string? expected) =>
        Assert.Equal(expected, LibraryLength.Short(minutes, category));

    [Fact]
    public void Engine_so_reconhecida_leva_o_aviso_no_chip()
    {
        var card = LibraryCards.Describe(Matched(Signature("Shirogane Station 9"), length: 4, minutes: 2520), Hidden);

        Assert.Equal("Siglus · sem suporte", card.Chip);
        Assert.True(card.ChipWarn);
        Assert.Null(card.ProgressPercent);
        Assert.Equal("Longa · 42 h", card.Subtitle);
    }

    [Fact]
    public void Capa_adulta_leva_o_selo_em_todo_modo_e_so_o_desfocar_pede_o_borrao()
    {
        var game = Matched(Signature("Kuroneko Archive", "KiriKiri"), adult: true);

        var blur = LibraryCards.Describe(game, Hidden);
        var shown = LibraryCards.Describe(game, Everything);
        var hide = LibraryCards.Describe(game, new LibraryPrivacy(Adult: AdultMode.Hide));

        Assert.All([blur, shown, hide], c => Assert.True(c.Adult));
        Assert.All([blur, shown, hide], c => Assert.Equal(LibraryCoverKind.Image, c.Cover));
        Assert.True(blur.CoverBlurred);
        Assert.False(shown.CoverBlurred);
        Assert.False(hide.CoverBlurred);
        Assert.Equal(LibraryMatchState.Matched, blur.Match);
        Assert.Equal(VndbImageVisibility.AdultLevel, blur.Image!.Sexual);
    }

    [Fact]
    public void Jogo_sem_casamento_mostra_a_placa_sem_capa_e_diz_sem_dado()
    {
        var game = Plain("Toge no Kaji") with { WindowTitle = "峠の鍛冶", SearchedAt = DateTimeOffset.UnixEpoch };

        var card = LibraryCards.Describe(game, Hidden);

        Assert.Equal(LibraryCoverKind.NoArt, card.Cover);
        Assert.Equal("Engine não reconhecida", card.Chip);
        Assert.True(card.ChipWarn);
        Assert.Equal(LibraryMatchState.Unmatched, card.Match);
        Assert.Equal("Sem casamento no VNDB", card.MatchText);
        Assert.Equal("峠の鍛冶", card.OriginalTitle);
        Assert.Equal("sem dado", card.Subtitle);
        Assert.Equal("Ainda não procurado no VNDB", LibraryCards.Describe(Plain("Outro"), Hidden).MatchText);
    }

    [Fact]
    public void Jogo_com_candidatos_espera_a_confirmacao_e_mostra_a_capa_do_primeiro()
    {
        var candidate = new LibraryCandidate("v7", "Kōrin no Mori", "光輪の森", "2021", 0.95, ["janela", "pasta"]) { Image = new VndbImageRef("https://t.vndb.org/cv/7.jpg") };
        var game = Signature("Kōrin no Mori", "Ren'Py") with { Candidates = [candidate] };

        var card = LibraryCards.Describe(game, Hidden);

        Assert.Equal(LibraryMatchState.NeedsConfirmation, card.Match);
        Assert.Equal("Confirmar o casamento", card.MatchText);
        Assert.Equal(LibraryCoverKind.Image, card.Cover);
        Assert.Equal("https://t.vndb.org/cv/7.jpg", card.Image!.Url);
        Assert.Equal("Kōrin no Mori", card.Title);
    }

    [Fact]
    public void Candidatos_dizem_de_onde_veio_o_casamento_e_o_que_o_VNDB_sabe_do_jogo()
    {
        var game = Signature("Kōrin no Mori", "Ren'Py") with
        {
            Exe = "game.exe",
            Candidates =
            [
                new LibraryCandidate("v7", "Kōrin no Mori", "光輪の森", "2021-03-02", 0.95, ["janela", "pasta", "exe"]) { Length = 2, LengthMinutes = 240, Rating = 76 },
                new LibraryCandidate("v8", "Kōrin no Mori: Gaiden", "光輪の森 外伝", "2022", 0.7, ["pasta"]) { Length = 1, LengthMinutes = 120 },
                new LibraryCandidate("v9", "Kōrin", null, null, 0.4, []),
            ],
        };

        var choices = LibraryMatchChoices.Describe(game);

        Assert.Equal(["v7", "v8", "v9"], choices.Select(c => c.VndbId));
        Assert.Equal("2021 · Curta · 4 h · nota 7,6", choices[0].Details);
        Assert.Equal([("Título da janela", true), ("Pasta", true), ("Exe: game.exe", false)], choices[0].Badges.Select(b => (b.Text, b.Strong)));
        Assert.Equal([("Pasta parecida", false)], choices[1].Badges.Select(b => (b.Text, b.Strong)));
        Assert.Equal([("Só o título", false)], choices[2].Badges.Select(b => (b.Text, b.Strong)));
        Assert.Equal(string.Empty, choices[2].Details);
    }

    [Fact]
    public void Gaveta_mostra_engine_vndb_duracao_ano_tags_e_o_andamento_do_projeto()
    {
        var game = Matched(Yuris("Amaoto no Hakoniwa")) with { ProjectPath = "C:\\p" };
        var tags = new UserTags();
        tags.Add(game.Id, "favorito");

        var drawer = LibraryDrawers.Describe(game, Hidden, tags, translation: new StatusCounts(29409, 0, 0, 18802, 0));

        Assert.Equal("Yu-RIS · traduz", drawer.EngineText);
        Assert.Equal("v31201 · casado", drawer.VndbText);
        Assert.Equal("Média · 18 h", drawer.Duration);
        Assert.Equal("2019", drawer.ReleasedYear);
        Assert.Equal(["Escola", "Mistério", "Chuva", "favorito"], drawer.Tags.Select(t => t.Text));
        Assert.Equal([false, false, false, true], drawer.Tags.Select(t => t.Mine));
        Assert.Equal(1, drawer.MoreTags);
        Assert.True(drawer.SpoilerTagsHidden);
        Assert.True(drawer.AdultTagsHidden);
        Assert.Equal("Numa cidade [spoiler escondido].", drawer.Synopsis);
        Assert.True(drawer.SynopsisHasSpoilers);
        Assert.Equal("18.802 de 48.211 linhas prontas", drawer.TranslationText);
    }

    [Fact]
    public void Gaveta_usa_a_sinopse_e_os_nomes_de_tag_em_portugues_da_ficha_e_marca_a_revisada()
    {
        var sheet = new LibrarySheet("Numa cidade onde chove.", true, [], new Dictionary<string, string> { ["g1"] = "Colégio" }, SynopsisReviewed: true);

        var drawer = LibraryDrawers.Describe(Matched(Yuris("Jogo")), Hidden, sheet: sheet);

        Assert.Equal("Numa cidade onde chove.", drawer.Synopsis);
        Assert.True(drawer.SynopsisReviewed);
        Assert.Equal("Colégio", drawer.Tags[0].Text);
        Assert.False(LibraryDrawers.Describe(Matched(Yuris("Jogo")), Hidden).SynopsisReviewed);
    }

    [Fact]
    public void Gaveta_de_jogo_sem_casamento_diz_o_que_falta()
    {
        var drawer = LibraryDrawers.Describe(Plain("Toge no Kaji"), Hidden);

        Assert.Equal("Não reconhecida", drawer.EngineText);
        Assert.Equal("sem casamento", drawer.VndbText);
        Assert.Equal("sem dado", drawer.Duration);
        Assert.Null(drawer.ReleasedYear);
        Assert.Null(drawer.Synopsis);
        Assert.Empty(drawer.Tags);
        Assert.Equal("Siglus · sem suporte ainda", LibraryDrawers.EngineText(new LibraryEngine("siglus", "Siglus", LibraryEngineSource.Signature)));
        Assert.Equal("a confirmar", LibraryDrawers.VndbText(Plain("x") with { Candidates = [new LibraryCandidate("v1", "t", null, null, 0.5, [])] }));
    }

    private static IReadOnlyList<LibraryGame> Collection() =>
    [
        Matched(Yuris("Amaoto no Hakoniwa")) with { ProjectPath = "C:\\p\\a" },
        Matched(Yuris("Hoshikuzu Diary")) with { ProjectPath = "C:\\p\\h" },
        Matched(Signature("Shirogane Station 9")),
        Matched(Signature("Yūgure Kissaten", "KiriKiri")),
        Plain("Toge no Kaji"),
        Matched(Yuris("Nanairo Ressha")) with { Portuguese = new LibraryPortuguese("v31201", PortugueseStatus.Complete, DateTimeOffset.UnixEpoch, []) },
    ];

    private static int? Percent(LibraryGame game) => game.Title switch
    {
        "Amaoto no Hakoniwa" => 38,
        "Hoshikuzu Diary" => 100,
        _ => null,
    };

    [Fact]
    public void Contagens_dos_filtros_seguem_o_quadro()
    {
        var counts = LibraryListing.Count(Collection(), Percent);

        Assert.Equal(6, counts.All);
        Assert.Equal(1, counts.InTranslation);
        Assert.Equal(3, counts.NoPortuguese);
        Assert.Equal(2, counts.Unsupported);
        Assert.Equal([6, 1, 3, 2], [counts.Of(LibraryFilter.All), counts.Of(LibraryFilter.InTranslation), counts.Of(LibraryFilter.NoPortuguese), counts.Of(LibraryFilter.Unsupported)]);
        var games = Collection();
        Assert.Equal(games.Count(g => g.Engine.Translatable), counts.Of(LibraryFilter.Supported));
        Assert.All(LibraryListing.Filter(games, LibraryFilter.Supported, null, Percent, null, Hidden), g => Assert.True(g.Engine.Translatable));
    }

    [Theory]
    [InlineData("yugure", "Yūgure Kissaten")]
    public void Busca_acha_por_titulo_original_tag_e_ignora_acento(string query, string expected)
    {
        var games = Collection();
        var tags = new UserTags();
        tags.Add(games[5].Id, "favorito");

        var found = LibraryListing.Filter(games, LibraryFilter.All, query, Percent, tags, Hidden);

        Assert.Contains(expected, found.Select(g => g.Title));
    }

    [Fact]
    public void Tabela_da_varredura_diz_suporte_e_estado_do_VNDB_de_cada_pasta()
    {
        var yuris = Yuris("amaoto_hakoniwa");
        Assert.Equal((LibraryScanSupport.Translates, LibraryScanVndb.Waiting), LibraryScanReport.Row(yuris) is { } w ? (w.Support, w.Vndb) : default);
        Assert.Equal(LibraryScanVndb.Searching, LibraryScanReport.Row(yuris, searching: true).Vndb);
        Assert.Equal(LibraryScanVndb.Matched, LibraryScanReport.Row(Matched(yuris)).Vndb);
        Assert.Equal(LibraryScanVndb.Confirm, LibraryScanReport.Row(yuris with { Candidates = [new LibraryCandidate("v1", "t", null, null, 0.5, [])] }).Vndb);
        Assert.Equal(LibraryScanVndb.NoMatch, LibraryScanReport.Row(yuris with { SearchedAt = DateTimeOffset.UnixEpoch }).Vndb);

        var unknown = LibraryScanReport.Row(Plain("toge_no_kaji"));
        Assert.Equal((LibraryScanSupport.NoEngine, "Não reconhecida", "toge_no_kaji"), (unknown.Support, unknown.Engine, unknown.Folder));
        Assert.Equal(LibraryScanSupport.NoSupportYet, LibraryScanReport.Row(Signature("shirogane_st9")).Support);
    }

    [Fact]
    public void Ate_agora_conta_reconhecidos_sem_suporte_nao_reconhecidos_e_a_confirmar()
    {
        var games = new[]
        {
            Yuris("a"),
            Yuris("b"),
            Signature("c"),
            Plain("d"),
            Signature("e") with { Candidates = [new LibraryCandidate("v1", "t", null, null, 0.5, [])] },
        };

        Assert.Equal(new LibraryScanTally(2, 2, 1, 1), LibraryScanReport.Tally(games));
    }

    [Fact]
    public void Pastas_da_biblioteca_guardam_a_escolha_da_primeira_varredura_e_vao_para_o_arquivo()
    {
        var library = new GameLibrary();
        var when = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

        var root = library.SetRoot(new LibraryRoot(Path.Combine(Path.GetTempPath(), "jogos"), IncludeSubfolders: false, RescanOnOpen: true, ScannedAt: when));
        library.SetRoot(new LibraryRoot(Path.Combine(Path.GetTempPath(), "outros")));
        library.SetRoot(root with { MatchVndb = false });

        Assert.Equal(2, library.Roots.Count);
        Assert.False(library.Roots[0].MatchVndb);
        Assert.Equal(when, library.LastScanAt);

        var back = LibraryFile.Parse(LibraryFile.Serialize(library));
        Assert.Equal(library.Roots, back.Roots);
        Assert.True(library.RemoveRoot(root.Path));
        Assert.False(library.RemoveRoot(root.Path));
        Assert.Single(library.Roots);
    }
}
