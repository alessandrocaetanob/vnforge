using VNForge.Core.Library;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Library.SyntheticGames;

namespace VNForge.Core.Tests.Library;

public class LibraryVisibilityTests
{
    private static LibraryGame Confirmed(bool adult, string? synopsis = "Começo [b]forte[/b]. [spoiler]O vilão é o irmão.[/spoiler] Fim, veja [url=https://vndb.org/v4]o site[/url].") =>
        LibraryMatcher.Confirm(
            new LibraryGame("g1", "pasta", null, LibraryEngine.Unknown, "pasta", null, DateTimeOffset.UnixEpoch),
            Vn(
                "v4",
                "Jogo",
                image: new VndbImage("https://example.test/capa.jpg", null, null, null, adult ? 1.8 : 0, 0),
                tags:
                [
                    new VndbTag("g1", "Drama", "cont", 2.9, 0, false),
                    new VndbTag("g2", "O irmão é o vilão", "cont", 2.5, 2, false),
                    .. adult ? [new VndbTag("g3", "Cena explícita", "ero", 2.7, 0, false)] : Array.Empty<VndbTag>(),
                ],
                description: synopsis),
            LibraryConfirmation.Manual);

    [Fact]
    public void Por_padrao_esconde_spoiler_e_conteudo_adulto()
    {
        var card = LibraryVisibility.Card(Confirmed(adult: true), new LibraryPrivacy())!;

        Assert.Null(card.CoverUrl);
        Assert.True(card.CoverHidden);
        Assert.True(card.Adult);
        Assert.Equal(["Drama"], card.Tags.Select(t => t.Name));
        Assert.Equal(2, card.HiddenTags);
        Assert.True(card.SynopsisHasSpoilers);
        Assert.Equal($"Começo forte. {LibraryVisibility.SpoilerPlaceholder} Fim, veja o site.", card.Synopsis);
    }

    [Fact]
    public void Mostrar_spoiler_libera_tags_e_sinopse_mas_nao_a_capa_adulta()
    {
        var card = LibraryVisibility.Card(Confirmed(adult: true), new LibraryPrivacy(ShowSpoilers: true))!;

        Assert.Equal(["Drama", "O irmão é o vilão"], card.Tags.Select(t => t.Name));
        Assert.Contains("O vilão é o irmão.", card.Synopsis);
        Assert.DoesNotContain("[spoiler]", card.Synopsis);
        Assert.False(card.SynopsisHasSpoilers);
        Assert.True(card.CoverHidden);
        Assert.Null(card.CoverUrl);
    }

    [Fact]
    public void Mostrar_adulto_libera_a_capa_e_as_tags_eroticas()
    {
        var card = LibraryVisibility.Card(Confirmed(adult: true), new LibraryPrivacy(ShowAdult: true))!;

        Assert.Equal("https://example.test/capa.jpg", card.CoverUrl);
        Assert.False(card.CoverHidden);
        Assert.Equal(["Drama", "Cena explícita"], card.Tags.Select(t => t.Name));
        Assert.Equal(1, card.HiddenTags);
    }

    [Fact]
    public void Jogo_que_nao_e_adulto_mostra_a_capa_por_padrao()
    {
        var card = LibraryVisibility.Card(Confirmed(adult: false), new LibraryPrivacy())!;

        Assert.Equal("https://example.test/capa.jpg", card.CoverUrl);
        Assert.False(card.CoverHidden);
    }

    [Fact]
    public void Jogo_sem_confirmacao_nao_tem_ficha_para_mostrar()
    {
        var game = new LibraryGame("g1", "pasta", null, LibraryEngine.Unknown, "pasta", null, DateTimeOffset.UnixEpoch);

        Assert.Null(LibraryVisibility.Card(game, new LibraryPrivacy()));
    }

    [Fact]
    public void Sinopse_em_texto_simples_tira_os_codigos_do_vndb()
    {
        var text = LibraryVisibility.PlainSynopsis("[b]Negrito[/b] e [i]itálico[/i]\n[quote]citação[/quote] [url=https://x.test]link[/url] [from Fonte]", showSpoilers: false, out var had);

        Assert.Equal("Negrito e itálico\ncitação link", text);
        Assert.False(had);
    }
}
