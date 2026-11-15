using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Library.TagTestGames;

namespace VNForge.Core.Tests.Library;

/// <summary>Os modelos que a tela da Biblioteca desenha: o cartão da grade, a gaveta, os candidatos do casamento, os filtros e a tabela da primeira varredura.</summary>
[Trait("Risco", "R9")]
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
}
