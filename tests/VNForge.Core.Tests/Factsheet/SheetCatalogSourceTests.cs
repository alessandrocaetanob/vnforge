using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Library;
using VNForge.Core.Tests.Projects;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetCatalogSourceTests : IDisposable
{
    private static readonly DateTimeOffset Generated = new(2026, 11, 9, 15, 30, 0, TimeSpan.Zero);

    private readonly TempFolder _folder = new();

    private SheetStore Store => new(_folder.Path);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private static LibraryGame Game() =>
        TagTestGames.Plain("Amaoto") with
        {
            VndbId = Game1,
            Confirmation = LibraryConfirmation.Manual,
            Info = new LibraryVndbInfo("Amaoto no Hakoniwa", null, "2020", null, "Original synopsis.", null, null, 0, [new LibraryTag("g10", "Comedy", "cont", 2.5, 0)], false),
        };

    private async Task Fetched() => await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);

    private async Task Translated()
    {
        await Fetched();
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
    }

    [Fact]
    public void Jogo_sem_ficha_buscada_ou_sem_jogo_do_VNDB_nao_tem_ficha()
    {
        var source = new SheetCatalogSource(Store);

        Assert.Null(source.SheetOf(Game()));
        Assert.Null(source.SheetOf(Game() with { VndbId = null }));
    }

    [Fact]
    public async Task Personagem_de_ficha_antiga_sem_as_edicoes_e_contado_e_vale_spoiler_2()
    {
        await Fetched();
        Store.Update(s => s.SaveCharacter(s.LoadCharacter("c1")! with { Appearances = [.. s.LoadCharacter("c1")!.Appearances.Select(a => a with { Editions = null, Spoiler = 0 })] }));
        var source = new SheetCatalogSource(Store);

        var sheet = source.SheetOf(Game())!;

        Assert.Equal(1, source.StaleCharacters);
        Assert.Equal(2, sheet.Characters.Single(c => c.Name == "Kenta Aizawa").Spoiler);
    }

    [Fact]
    public async Task Ficha_so_buscada_traz_o_original_e_nenhum_nome_de_tag_traduzido()
    {
        await Fetched();

        var sheet = new SheetCatalogSource(Store).SheetOf(Game())!;

        Assert.False(sheet.SynopsisTranslated);
        Assert.StartsWith("A rainy town.", sheet.Synopsis, StringComparison.Ordinal);
        Assert.Empty(sheet.TagNames);
        Assert.Equal(["Kenta Aizawa", "Aoi Sakura", "Ghost"], sheet.Characters.Select(c => c.Name));
        Assert.All(sheet.Characters.Where(c => c.Description is not null), c => Assert.False(c.Translated));
    }

    [Fact]
    public async Task Ficha_traduzida_traz_a_sinopse_os_personagens_e_os_nomes_em_portugues_com_as_marcas_dos_tracos()
    {
        await Translated();

        var sheet = new SheetCatalogSource(Store).SheetOf(Game())!;

        Assert.True(sheet.SynopsisTranslated);
        Assert.StartsWith("PT: A rainy town.", sheet.Synopsis, StringComparison.Ordinal);
        Assert.Equal("PT: Comedy", sheet.TagNames["g10"]);
        var kenta = sheet.Characters[0];
        Assert.Equal("protagonista", kenta.Role);
        Assert.True(kenta.Translated);
        Assert.StartsWith("PT: The hero.", kenta.Description, StringComparison.Ordinal);
        Assert.Equal(2, sheet.Characters[2].Spoiler);
        var adult = kenta.Traits.Single(t => t.Name == "PT: Virgin Sex");
        Assert.True(adult.Sexual);
        Assert.True(kenta.Traits.Single(t => t.Name == "PT: Tsundere").Lie);
        Assert.Equal(1, kenta.Traits.Single(t => t.Name == "PT: Amnesia").Spoiler);
    }

    [Fact]
    public async Task O_catalogo_com_a_ficha_esconde_o_traco_adulto_e_o_spoiler_e_sem_a_ficha_usa_o_original()
    {
        await Translated();
        var game = Game();

        var hidden = LibraryCatalog.Render(new GameLibrary([game]), new PlayLog(), Generated, null, new SheetCatalogSource(Store), _ => null).Html;
        var adult = LibraryCatalog.Render(new GameLibrary([game]), new PlayLog(), Generated, new LibraryCatalogOptions(new LibraryPrivacy(false, true)), new SheetCatalogSource(Store), _ => null).Html;
        var without = LibraryCatalog.Render(new GameLibrary([game]), new PlayLog(), Generated, null, null, _ => null).Html;

        Assert.Contains("PT: A rainy town.", hidden, StringComparison.Ordinal);
        Assert.Contains("PT: Comedy", hidden, StringComparison.Ordinal);
        Assert.Contains("PT: Long Hair", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Virgin Sex", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Amnesia", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghost", hidden, StringComparison.Ordinal);
        Assert.Contains("PT: Virgin Sex", adult, StringComparison.Ordinal);
        Assert.Contains("Original synopsis.", without, StringComparison.Ordinal);
        Assert.DoesNotContain("PT:", without, StringComparison.Ordinal);
    }
}
