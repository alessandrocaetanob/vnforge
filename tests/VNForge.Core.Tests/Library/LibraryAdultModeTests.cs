using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Library;

public class LibraryAdultModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Func<LibraryGame, int?> NoProject = _ => null;

    private static IReadOnlyList<LibraryGame> Library => [TagTestGames.WithVndb("Jogo comum"), TagTestGames.WithVndb("Jogo adulto", adult: true)];

    private static LibraryCatalogResult Catalog(AdultMode mode) =>
        LibraryCatalog.Render(new GameLibrary(Library, new LibraryPrivacy(Adult: mode)), new PlayLog(), Now, null, null, _ => null);

    [Theory]
    [InlineData(0.0, true, true, false)]
    public void A_capa_borrada_e_o_selo_seguem_a_mesma_nota_da_imagem_que_a_tela_usa_para_borrar(double sexual, bool infoAdult, bool adult, bool blurred)
    {
        var candidate = new LibraryCandidate("v7", "Candidato", null, null, 0.9, ["pasta"]) { Image = new VndbImageRef("https://t.vndb.org/cv/7.jpg", sexual) };
        var game = new LibraryGame("g1", "C:\\Jogos\\Candidato", null, LibraryEngine.Unknown, "Candidato", null, Now) { Candidates = [candidate] };
        if (infoAdult)
        {
            game = TagTestGames.WithVndb("Candidato", adult: true);
        }

        var card = LibraryCards.Describe(game, new LibraryPrivacy(Adult: AdultMode.Blur));

        Assert.Equal(adult, card.Adult);
        Assert.Equal(blurred, card.CoverBlurred);
        Assert.Equal(blurred, card.Image is { } image && VndbImageVisibility.IsBlurred(image, new LibraryPrivacy(Adult: AdultMode.Blur)));
    }

    [Fact]
    public void Esconder_tira_o_jogo_adulto_da_grade_da_busca_dos_contadores_da_fila_e_do_catalogo()
    {
        var hide = new LibraryPrivacy(Adult: AdultMode.Hide);

        var grid = LibraryListing.Filter(Library, LibraryFilter.All, null, NoProject, null, hide);
        var search = LibraryListing.Filter(Library, LibraryFilter.All, "adulto", NoProject, null, hide);
        var counts = LibraryListing.Count(Library, NoProject, hide);
        var queue = TranslationQueue.Build(Library, hide);
        var listed = hide.Listed(Library);
        var catalog = Catalog(AdultMode.Hide);

        Assert.Equal(["Jogo comum"], grid.Select(g => g.Title));
        Assert.Empty(search);
        Assert.Equal(1, counts.All);
        Assert.Equal(["Jogo comum"], queue.Select(e => e.Game.Title));
        Assert.Equal(["Jogo comum"], listed.Select(g => g.Title));
        Assert.Equal(1, catalog.Games);
        Assert.DoesNotContain("Jogo adulto", catalog.Html);
    }

    [Fact]
    public void Imagem_adulta_baixa_no_desfocar_e_no_mostrar_e_nao_no_esconder()
    {
        var image = new VndbImageRef("https://t.vndb.org/ch/1.jpg", Sexual: 1.5);

        Assert.True(VndbImageVisibility.MayLoad(image, new LibraryPrivacy(Adult: AdultMode.Show)));
        Assert.True(VndbImageVisibility.MayLoad(image, new LibraryPrivacy(Adult: AdultMode.Blur)));
        Assert.False(VndbImageVisibility.MayLoad(image, new LibraryPrivacy(Adult: AdultMode.Hide)));
        Assert.True(VndbImageVisibility.IsBlurred(image, new LibraryPrivacy(Adult: AdultMode.Blur)));
        Assert.False(VndbImageVisibility.IsBlurred(image, new LibraryPrivacy(Adult: AdultMode.Show)));
        Assert.True(VndbImageVisibility.MayLoad(new VndbImageRef("https://t.vndb.org/ch/2.jpg"), new LibraryPrivacy(Adult: AdultMode.Hide)));
    }
}
