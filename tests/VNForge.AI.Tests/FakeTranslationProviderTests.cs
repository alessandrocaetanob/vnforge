using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

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

    [Fact]
    public async Task Traducao_e_deterministica_e_preserva_os_marcadores()
    {
        var provider = new FakeTranslationProvider();
        var batch = new TranslationBatch([Unit("a", @"こんにちは\n{VAR:name}\p")]);

        var first = await provider.TranslateAsync(batch, CancellationToken.None);
        var second = await new FakeTranslationProvider().TranslateAsync(batch, CancellationToken.None);

        Assert.Equal(first[0].Translation, second[0].Translation);
        Assert.Contains(@"\n{VAR:name}\p", first[0].Translation);
        Assert.NotEqual(@"こんにちは\n{VAR:name}\p", first[0].Translation);
    }

    [Fact]
    public async Task Respeita_o_cancelamento()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FakeTranslationProvider().TranslateAsync(new TranslationBatch([Unit("a", "x")]), cts.Token));
    }

    [Fact]
    public void Lote_recusa_linhas_nulas()
    {
        Assert.Throws<ArgumentNullException>(() => new TranslationBatch(null!));
    }
}
