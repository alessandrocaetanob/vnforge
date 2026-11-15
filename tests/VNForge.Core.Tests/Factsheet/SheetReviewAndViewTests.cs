using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

[Trait("Risco", "R9")]
public sealed class SheetReviewAndViewTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetReviewAndViewTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Translated()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
    }

    private GameSheetCard Card(LibraryPrivacy? privacy = null)
    {
        var game = Store.LoadGame(Game1)!;
        return SheetViewer.Card(game, Store.LoadCharacters(game.CharacterIds), Store.LoadTerms(), privacy ?? new LibraryPrivacy());
    }

    [Fact]
    public async Task Conteudo_adulto_some_por_padrao_retrato_tag_erotica_e_traco_sexual()
    {
        await Translated();

        var hidden = Card(new LibraryPrivacy(Adult: AdultMode.Hide));
        var blurred = Card();
        var shown = Card(new LibraryPrivacy(Adult: AdultMode.Show, ShowSpoilers: true));

        var aoi = hidden.Characters[1];
        Assert.Equal((null, true), (aoi.Image, aoi.ImageHidden));
        Assert.NotNull(blurred.Characters[1].Image);
        Assert.True(blurred.Characters[1].ImageHidden);
        Assert.NotNull(hidden.Characters[0].Image);
        Assert.DoesNotContain(hidden.Tags, t => t.Category == "ero");
        Assert.DoesNotContain(hidden.Characters[0].Traits, t => t.Id == Adult.Id);
        Assert.NotNull(shown.Characters[1].Image);
        Assert.Contains(shown.Tags, t => t.Category == "ero");
        Assert.Contains(shown.Characters[0].Traits, t => t.Id == Adult.Id);
    }
}
