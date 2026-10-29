using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class LineShortenerTests
{
    private static readonly TextUnit Linha = new("yst00001:000000", TextUnitKind.Dialogue, "美咲", @"原文\p", @"Uma tradução longa demais para a caixa\p", TranslationStatus.Machine);

    private static readonly WrapRule Caixa = new(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform);

    private static string Palavras(int quantas) => string.Join(' ', Enumerable.Repeat("aaaaaaaaa", quantas));

    [Fact]
    public async Task Pede_a_versao_curta_pelo_caminho_da_reescrita_com_a_traducao_de_hoje_e_o_tamanho_da_caixa()
    {
        var provedor = new FakeTranslationProvider();

        var curta = await new LineSuggester(() => provedor).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken);

        Assert.StartsWith("[Shorter 1]", curta);
        var (lote, pedido) = Assert.Single(provedor.ReceivedRewrites);
        Assert.Equal([Linha.Id], lote.Lines.Select(l => l.Id));
        Assert.Equal(new RewriteRequest(RewriteDirection.Shorter, Linha.Translation!, 3, 40), pedido);
    }

    [Fact]
    public async Task Escolhe_a_primeira_versao_que_cabe_na_caixa()
    {
        var provedor = new Reescritor(Palavras(20) + @"\p", @"Cabe.\p");

        var curta = await new LineSuggester(() => provedor).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken);

        Assert.Equal(@"Cabe.\p", curta);
    }

    [Fact]
    public async Task Se_nenhuma_cabe_devolve_a_primeira_para_o_aviso_conferir()
    {
        var provedor = new Reescritor(Palavras(20) + @"\p", Palavras(30) + @"\p");

        var curta = await new LineSuggester(() => provedor).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken);

        Assert.Equal(Palavras(20) + @"\p", curta);
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
            new LineSuggester(() => new Reescritor(refused: true)).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken));

        Assert.Contains("recus", erro.Message);
    }

    [Fact]
    public async Task Versao_que_perde_o_marcador_sai_da_lista_e_sem_nenhuma_aproveitavel_vira_mensagem()
    {
        var erro = await Assert.ThrowsAsync<LineSuggestionException>(() =>
            new LineSuggester(() => new Reescritor("sem o marcador", "tambem sem")).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken));

        Assert.Contains("aproveitável", erro.Message);
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

    private sealed class Reescritor(params string[] opcoes) : ITranslationProvider, IRewriteProvider
    {
        private readonly bool refused;

        public Reescritor(bool refused)
            : this() => this.refused = refused;

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RewriteResult> RewriteAsync(TranslationBatch batch, RewriteRequest request, CancellationToken ct) =>
            Task.FromResult(new RewriteResult(batch.Lines[0].Id, opcoes, refused));
    }
}
