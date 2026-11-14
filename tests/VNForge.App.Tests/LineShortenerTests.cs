using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R4")]
public sealed class LineShortenerTests
{
    private static readonly TextUnit Linha = new("yst00001:000000", TextUnitKind.Dialogue, "美咲", @"原文\p", @"Uma tradução longa demais para a caixa\p", TranslationStatus.Machine);

    private static readonly WrapRule Caixa = new(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform);

    [Fact]
    public async Task Versao_que_perde_o_marcador_sai_da_lista_e_sem_nenhuma_aproveitavel_vira_mensagem()
    {
        var erro = await Assert.ThrowsAsync<LineSuggestionException>(() =>
            new LineSuggester(() => new Reescritor("sem o marcador", "tambem sem")).ShortenAsync(Linha, Linha.Translation!, Caixa, TestContext.Current.CancellationToken));

        Assert.Contains("aproveitável", erro.Message);
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
