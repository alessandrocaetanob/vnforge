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
    public async Task Soma_o_uso_de_tokens_das_chamadas()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"reviews":[{"id":"a","suggestion":""}]}""", new ClaudeUsage(10, 5, 0, 0)));
        var provider = new ClaudeReviewProvider(client);

        await provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), CancellationToken.None);
        await provider.ReviewAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), CancellationToken.None);

        Assert.Equal(new ClaudeUsage(20, 10, 0, 0), provider.Usage);
    }
}
