using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

[Trait("Risco", "R5")]
public class FakeTranslationProviderTests
{
    private static TextUnit Unit(string id, string source) =>
        new(id, TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    [Fact]
    public async Task Devolve_um_resultado_por_linha_na_ordem_do_lote()
    {
        var provider = new FakeTranslationProvider();
        var batch = new TranslationBatch([Unit("a", "こんにちは"), Unit("b", "さようなら")]);

        var results = await provider.TranslateAsync(batch, CancellationToken.None);

        Assert.Equal(["a", "b"], results.Select(r => r.Id));
    }
}
