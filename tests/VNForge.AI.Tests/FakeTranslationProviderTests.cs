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
    public async Task Registra_os_lotes_recebidos()
    {
        var provider = new FakeTranslationProvider();
        var batch = new TranslationBatch([Unit("a", "x")]) { StyleGuide = "informal" };

        await provider.TranslateAsync(batch, CancellationToken.None);

        Assert.Same(batch, Assert.Single(provider.ReceivedBatches));
    }

    [Fact]
    public async Task Lote_vazio_devolve_lista_vazia()
    {
        var results = await new FakeTranslationProvider().TranslateAsync(new TranslationBatch([]), CancellationToken.None);

        Assert.Empty(results);
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
    public async Task Tradutor_customizado_substitui_o_padrao()
    {
        var provider = new FakeTranslationProvider(u => $"<{u.Id}>");

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), CancellationToken.None);

        Assert.Equal("<a>", results[0].Translation);
    }

    [Fact]
    public async Task Falha_programada_lanca_na_chamada_pedida()
    {
        var provider = new FakeTranslationProvider { FailOnCall = 2 };
        var batch = new TranslationBatch([Unit("a", "x")]);

        await provider.TranslateAsync(batch, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateAsync(batch, CancellationToken.None));
    }

    [Fact]
    public void Lote_recusa_linhas_nulas()
    {
        Assert.Throws<ArgumentNullException>(() => new TranslationBatch(null!));
    }
}
