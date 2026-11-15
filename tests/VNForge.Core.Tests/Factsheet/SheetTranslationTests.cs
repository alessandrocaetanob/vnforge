using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

[Trait("Risco", "R5")]
public sealed class SheetTranslationTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetTranslationTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Fetch(FakeVndbCatalog? catalog = null, string id = Game1) =>
        await SheetFetcher.FetchAsync(catalog ?? Catalog(), Store, id, cancellationToken: Ct);

    [Fact]
    public async Task Lote_que_falha_nao_perde_os_outros_lotes()
    {
        await Fetch();
        var calls = 0;
        var translator = new ThrowingOnceTranslator(() => calls++ == 0);

        var result = await new SheetTranslationRunner(translator, Store).RunAsync(Game1, cancellationToken: Ct);

        var failure = Assert.Single(result.Failures);
        Assert.Contains("recusou", failure.Message, StringComparison.Ordinal);
        Assert.Equal(14 - failure.Keys.Count, result.Translated);
        Assert.True(result.Calls >= 2);
    }

    private sealed class ThrowingOnceTranslator(Func<bool> fail) : ISheetTranslator
    {
        public Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct) =>
            fail()
                ? throw new InvalidOperationException("A IA recusou o pedido.")
                : Task.FromResult<IReadOnlyList<SheetTranslation>>([.. request.Items.Select(i => new SheetTranslation(i.Key, "PT: " + i.Source))]);
    }
}
