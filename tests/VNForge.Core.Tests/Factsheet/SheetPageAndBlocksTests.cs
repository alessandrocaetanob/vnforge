using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

/// <summary>A ficha na tela (d9, #554): spoiler por nível, rótulos, blocos da revisão, tradução de um bloco e a leitura dos arquivos.</summary>
[Trait("Risco", "R7")]
public sealed class SheetPageAndBlocksTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetPageAndBlocksTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Translated(VndbVisualNovel? game = null)
    {
        await SheetFetcher.FetchAsync(Catalog(game ?? Game() with { Rating = 79 }), Store, Game1, cancellationToken: Ct);
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
    }

    private GameSheetCard Card(SheetVisibility visibility)
    {
        var game = Store.LoadGame(Game1)!;
        return SheetViewer.Card(game, Store.LoadCharacters(game.CharacterIds), Store.LoadTerms(), visibility);
    }

    private IReadOnlyList<SheetBlock> Blocks(SheetVisibility visibility) => SheetBlocks.Build(Store, Game1, visibility);

    [Fact]
    public async Task A_ficha_ilegivel_recusa_em_vez_de_abrir_vazia()
    {
        await Translated();
        await File.WriteAllTextAsync(Store.GamePath(Game1), "{ não é json", Ct);

        Assert.Throws<LibraryException>(() => SheetPage.Load(_folder.Path, Game1));
    }
}
