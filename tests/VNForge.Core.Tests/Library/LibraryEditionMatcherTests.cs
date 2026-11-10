using VNForge.Core.Library;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

public sealed class LibraryEditionMatcherTests
{
    private static LibraryGame GameIn(string folderName, string? windowTitle = null) =>
        Owned("g1", folderName, Moon, "Moon Flower") with { WindowTitle = windowTitle };

    [Fact]
    public void Casa_a_edicao_cujo_titulo_se_parece_com_a_pasta()
    {
        var releases = new[] { Release("r1", "Moon Flower"), Release("r2", "Moon Flower Deluxe Pack") };

        Assert.Equal(["r1"], LibraryEditionMatcher.Match(GameIn("Moon Flower"), releases));
        Assert.Equal(["r2"], LibraryEditionMatcher.Match(GameIn("Moon Flower Deluxe Pack"), releases));
    }

    [Fact]
    public void Casa_pelo_titulo_da_janela_e_pelo_titulo_de_um_idioma_da_edicao()
    {
        var japanese = Release("r5", "Tsuki no Hana") with { Languages = [new VndbReleaseLanguage("ja", "月の花", "Tsuki no Hana", false, true)] };

        Assert.Equal(["r5"], LibraryEditionMatcher.Match(GameIn("jogo", "月の花"), [japanese, Release("r1", "Outro")]));
    }

    [Fact]
    public void Edicoes_empatadas_valem_todas()
    {
        var releases = new[] { Release("r1", "Moon Flower"), Release("r7", "Moon Flower") };

        Assert.Equal(["r1", "r7"], LibraryEditionMatcher.Match(GameIn("Moon Flower"), releases));
    }

    [Fact]
    public void Patch_e_edicao_de_outro_jogo_ficam_de_fora()
    {
        var releases = new[] { Release("r4", "Moon Flower", patch: true), Release("r3", "Moon Flower", Star) };

        Assert.Empty(LibraryEditionMatcher.Match(GameIn("Moon Flower"), releases));
    }

    [Fact]
    public void Nada_se_parece_o_bastante_ou_jogo_sem_VNDB_ou_pasta_generica_dao_lista_vazia()
    {
        Assert.Empty(LibraryEditionMatcher.Match(GameIn("Moon Flower"), [Release("r9", "Completely Different Title")]));
        Assert.Empty(LibraryEditionMatcher.Match(GameIn("Moon Flower") with { VndbId = null }, [Release("r1", "Moon Flower")]));
        Assert.Empty(LibraryEditionMatcher.Match(GameIn("game"), [Release("r1", "Moon Flower")]));
    }
}
