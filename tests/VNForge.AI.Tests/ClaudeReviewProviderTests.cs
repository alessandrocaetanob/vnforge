using VNForge.AI.Claude;
using VNForge.AI.Review;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class ClaudeReviewProviderTests
{
    private sealed class StubClient(Func<ClaudeRequest, ClaudeReply> reply) : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(reply(request));
        }
    }

    private static TextUnit Unit(string id, string source, string translation) =>
        new(id, TextUnitKind.Dialogue, null, source, translation, TranslationStatus.Machine);

    private static ClaudeReply Ok(string json) => new("end_turn", json);

    [Fact]
    public async Task Devolve_uma_sugestao_por_linha_na_ordem_do_lote()
    {
        var client = new StubClient(_ => Ok("""{"reviews":[{"id":"b","suggestion":" Use \"tchau\". "},{"id":"a","suggestion":""}]}"""));
        var provider = new ClaudeReviewProvider(client);

        var results = await provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi"), Unit("b", "さよなら", "Adeus")]), CancellationToken.None);

        Assert.Equal([new ReviewResult("a", string.Empty), new ReviewResult("b", "Use \"tchau\".")], results);
    }

    [Fact]
    public async Task O_pedido_leva_original_e_traducao_e_o_schema_de_revisao()
    {
        var client = new StubClient(_ => Ok("""{"reviews":[{"id":"a","suggestion":""}]}"""));
        var provider = new ClaudeReviewProvider(client);

        await provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains("やあ", request.User);
        Assert.Contains("\"translation\":\"Oi\"", request.User);
        Assert.Contains("revisa", request.System);
        Assert.Contains("\"reviews\"", request.JsonSchema);
    }

    [Fact]
    public async Task Ids_que_nao_batem_com_o_lote_lancam()
    {
        var client = new StubClient(_ => Ok("""{"reviews":[{"id":"x","suggestion":""}]}"""));
        var provider = new ClaudeReviewProvider(client);

        await Assert.ThrowsAsync<ClaudeResponseException>(() =>
            provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), CancellationToken.None));
    }

    [Fact]
    public async Task Recusa_isola_a_linha_e_mantem_as_demais()
    {
        ClaudeReply Reply(ClaudeRequest request)
        {
            if (request.User.Contains("proibida", StringComparison.Ordinal))
                return new ClaudeReply("refusal", string.Empty);
            if (request.User.Contains("\"id\":\"b\"", StringComparison.Ordinal) && !request.User.Contains("\"id\":\"a\"", StringComparison.Ordinal))
                return Ok("""{"reviews":[{"id":"b","suggestion":"ok"}]}""");
            return Ok("""{"reviews":[{"id":"a","suggestion":"ok"},{"id":"b","suggestion":"ok"}]}""");
        }

        var client = new StubClient(Reply);
        var provider = new ClaudeReviewProvider(client);

        var results = await provider.ReviewAsync(new TranslationBatch([Unit("a", "proibida", "x"), Unit("b", "やあ", "Oi")]), CancellationToken.None);

        Assert.True(results[0].Refused);
        Assert.Equal(new ReviewResult("b", "ok"), results[1]);
    }

    [Fact]
    public async Task Soma_o_uso_de_tokens_das_chamadas()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"reviews":[{"id":"a","suggestion":""}]}""", new ClaudeUsage(10, 5, 0, 0)));
        var provider = new ClaudeReviewProvider(client);

        await provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), CancellationToken.None);
        await provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), CancellationToken.None);

        Assert.Equal(new ClaudeUsage(20, 10, 0, 0), provider.Usage);
    }
}
