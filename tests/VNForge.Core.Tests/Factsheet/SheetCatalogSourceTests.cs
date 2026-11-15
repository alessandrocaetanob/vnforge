using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Library;
using VNForge.Core.Tests.Projects;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

[Trait("Risco", "R9")]
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
    public async Task O_catalogo_com_a_ficha_esconde_o_traco_adulto_e_o_spoiler_e_sem_a_ficha_usa_o_original()
    {
        await Translated();
        var game = Game();

        var hidden = LibraryCatalog.Render(new GameLibrary([game]), new PlayLog(), Generated, null, new SheetCatalogSource(Store), _ => null).Html;
        var adult = LibraryCatalog.Render(new GameLibrary([game]), new PlayLog(), Generated, new LibraryCatalogOptions(new LibraryPrivacy(false, AdultMode.Show)), new SheetCatalogSource(Store), _ => null).Html;
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
