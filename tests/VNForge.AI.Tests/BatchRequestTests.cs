using VNForge.AI.BatchRequest;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

[Trait("Risco", "R5")]
public class BatchRequestTests
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

    private static TextUnit Unit(string id, string translation, string? speaker = "健太") =>
        new(id, TextUnitKind.Dialogue, speaker, $"原文{id}", translation, TranslationStatus.Machine);

    private static BatchRequestRunner Runner(IBatchRequestProvider provider) =>
        new(provider, new TranslationBatchBuilder([], new CharacterBook([])), new TranslationRunOptions { MaxRetries = 0, Delay = (_, _) => Task.CompletedTask });

    private static List<BatchCandidate> Candidates(params TextUnit[] units) =>
        [.. units.Select((u, i) => new BatchCandidate("cena01", i + 1, u))];

    private static ClaudeReply Ok(string json) => new("end_turn", json);

    [Fact]
    public async Task Recusa_do_lote_inteiro_vira_recusa_de_cada_linha_isolada()
    {
        var client = new StubClient(request =>
            request.User.Contains("Você vai?") ? new ClaudeReply("refusal", string.Empty) : Ok("""{"rewrites":[{"id":"a","translation":"Tu sabes?"}]}"""));
        var provider = new ClaudeBatchRequestProvider(client);

        var results = await provider.RewriteAsync("trocar você por tu", new TranslationBatch([Unit("a", "Você sabe?"), Unit("c", "Você vai?")]), CancellationToken.None);

        Assert.Equal([new BatchRewrite("a", "Tu sabes?"), new BatchRewrite("c", string.Empty, Refused: true)], results);
    }

    [Fact]
    public async Task Linha_que_a_IA_nao_devolveu_nao_gera_segunda_chamada_paga()
    {
        var provider = FakeBatchRequestProvider.Replacing("você", "tu");
        var runner = Runner(provider);

        var outcome = await runner.RunAsync("trocar você por tu", Candidates(Unit("a", "Você sabe?"), Unit("b", "Oi."), Unit("c", "Tchau.")), CancellationToken.None);

        Assert.Single(provider.Received);
        Assert.Single(outcome.Items);
        Assert.Equal(2, outcome.Unchanged);
    }
}
