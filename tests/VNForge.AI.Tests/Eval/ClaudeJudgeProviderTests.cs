using VNForge.AI.Claude;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Eval;

public sealed class ClaudeJudgeProviderTests
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
        new(id, TextUnitKind.Dialogue, "Misaki", source, translation, TranslationStatus.Untranslated);

    private static ClaudeReply Ok(string json) => new("end_turn", json);

    [Fact]
    public async Task Devolve_uma_nota_por_linha_na_ordem_do_lote_e_limita_a_escala()
    {
        var client = new StubClient(_ => Ok("""{"scores":[{"id":"b","score":14},{"id":"a","score":7.5}]}"""));
        var provider = new ClaudeJudgeProvider(client);

        var scores = await provider.JudgeAsync(new TranslationBatch([Unit("a", "やあ", "Oi"), Unit("b", "さよなら", "Adeus")]), TestContext.Current.CancellationToken);

        Assert.Equal([new JudgeScore("a", 7.5), new JudgeScore("b", 10)], scores);
    }

    [Fact]
    public async Task O_pedido_leva_original_e_texto_mas_nada_que_identifique_a_configuracao()
    {
        var client = new StubClient(_ => Ok("""{"scores":[{"id":"a","score":8}]}"""));
        var options = new ClaudeOptions { Model = ClaudeModels.Opus, Effort = ClaudeEffort.High };
        var provider = new ClaudeJudgeProvider(client, options);

        await provider.JudgeAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), TestContext.Current.CancellationToken);

        var request = Assert.Single(client.Requests);
        Assert.Contains("やあ", request.User);
        Assert.Contains("\"translation\":\"Oi\"", request.User);
        Assert.Contains("\"scores\"", request.JsonSchema);
        var text = request.FullSystem + request.User;
        Assert.DoesNotContain("claude", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("effort", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configura", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resposta_que_nao_bate_com_os_ids_do_lote_e_um_erro()
    {
        var client = new StubClient(_ => Ok("""{"scores":[{"id":"x","score":8}]}"""));
        var provider = new ClaudeJudgeProvider(client);

        await Assert.ThrowsAsync<ClaudeResponseException>(() =>
            provider.JudgeAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Soma_os_tokens_das_chamadas()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"scores":[{"id":"a","score":8}]}""", new ClaudeUsage(100, 10, 0, 0)));
        var provider = new ClaudeJudgeProvider(client);

        await provider.JudgeAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), TestContext.Current.CancellationToken);
        await provider.JudgeAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), TestContext.Current.CancellationToken);

        Assert.Equal(new ClaudeUsage(200, 20, 0, 0), provider.Usage);
    }

    [Fact]
    public async Task Recusa_do_modelo_deixa_so_a_linha_recusada_sem_nota()
    {
        var calls = 0;
        var client = new StubClient(_ => ++calls == 1 ? new ClaudeReply("refusal", string.Empty) : Ok("""{"scores":[{"id":"b","score":6}]}"""));
        var provider = new ClaudeJudgeProvider(client);

        var scores = await provider.JudgeAsync(new TranslationBatch([Unit("a", "やあ", "Oi")]), TestContext.Current.CancellationToken);

        Assert.Equal([new JudgeScore("a", 0, Refused: true)], scores);
    }
}
