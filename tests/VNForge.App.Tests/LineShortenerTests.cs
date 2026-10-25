using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class LineShortenerTests
{
    private static readonly TextUnit Linha = new("yst00001:000000", TextUnitKind.Dialogue, "美咲", @"原文\p", @"Uma tradução longa demais para a caixa\p", TranslationStatus.Machine);

    private static readonly WrapRule Caixa = new(Enabled: true, MaxWidth: 40, MaxLines: 3);

    [Fact]
    public async Task Pede_a_versao_curta_com_a_traducao_de_hoje_e_o_tamanho_da_caixa()
    {
        var provedor = new FakeTranslationProvider(u => @"Tradução curta\p");

        var curta = await new LineSuggester(() => provedor).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken);

        Assert.Equal(@"Tradução curta\p", curta);
        var lote = Assert.Single(provedor.ReceivedBatches);
        Assert.Equal([Linha.Id], lote.Lines.Select(l => l.Id));
        Assert.Equal(new ShortenRequest(Linha.Translation!, 3, 40), lote.Shorten[Linha.Id]);
    }

    [Fact]
    public async Task Sem_chave_explica_que_a_ia_precisa_da_chave_e_nao_inventa_texto()
    {
        var erro = await Assert.ThrowsAsync<LineSuggestionException>(() =>
            new LineSuggester(() => null).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken));

        Assert.Contains("chave", erro.Message);
    }

    [Fact]
    public async Task Recusa_da_ia_vira_mensagem_em_portugues()
    {
        var erro = await Assert.ThrowsAsync<LineSuggestionException>(() =>
            new LineSuggester(() => new Recusa()).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken));

        Assert.Contains("recus", erro.Message);
    }

    [Fact]
    public async Task Marcador_perdido_na_versao_curta_esgota_as_tentativas_e_vira_mensagem()
    {
        var provedor = new FakeTranslationProvider(u => "sem o marcador");

        var erro = await Assert.ThrowsAsync<LineSuggestionException>(() =>
            new LineSuggester(() => provedor).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken));

        Assert.Equal(3, provedor.ReceivedBatches.Count);
        Assert.Contains("recus", erro.Message);
    }

    [Fact]
    public async Task Linha_ou_caixa_nulas_sao_recusadas()
    {
        var suggester = new LineSuggester(() => null);
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() => suggester.ShortenAsync(null!, "x", Caixa, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => suggester.ShortenAsync(Linha, null!, Caixa, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => suggester.ShortenAsync(Linha, "x", null!, ct));
    }

    private sealed class Recusa : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>([.. batch.Lines.Select(l => new TranslationResult(l.Id, string.Empty, Refused: true))]);
    }
}
