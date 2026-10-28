using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class ClaudeRewriteTests
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

    private static TextUnit Unit(string id, string source, string? speaker = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static ClaudeReply Ok(string json) => new("end_turn", json);

    [Fact]
    public async Task Devolve_as_duas_opcoes_da_linha()
    {
        var client = new StubClient(_ => Ok("""{"rewrites":[{"id":"a","optionA":"Oi!","optionB":" E aí! "}]}"""));
        var provider = new ClaudeTranslationProvider(client);

        var result = await provider.RewriteAsync(new TranslationBatch([Unit("a", "やあ")]), new RewriteRequest(RewriteDirection.MoreNatural, "Olá, tudo bem?"), CancellationToken.None);

        Assert.Equal(new RewriteResult("a", ["Oi!", "E aí!"]), result, new RewriteResultComparer());
        Assert.False(result.Refused);
    }

    [Fact]
    public async Task Pedido_leva_a_direcao_a_traducao_de_hoje_e_a_caixa_do_jogo()
    {
        var client = new StubClient(_ => Ok("""{"rewrites":[{"id":"a","optionA":"A","optionB":"B"}]}"""));
        var provider = new ClaudeTranslationProvider(client);
        var batch = new TranslationBatch([Unit("a", "やあ", "美咲")]) { Characters = [new CharacterInfo("美咲", "Misaki", "seco e resmungão", "female")] };

        await provider.RewriteAsync(batch, new RewriteRequest(RewriteDirection.AsCharacter, "Olá, como você está passando hoje?", 3, 40), CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains("como o personagem falaria", request.BatchContext);
        Assert.Contains("no máximo 3 linhas de 40 colunas", request.BatchContext);
        Assert.Contains("tom: seco e resmungão", request.BatchContext);
        Assert.DoesNotContain("como o personagem falaria", request.System);
        Assert.Contains("\"current\":\"Olá, como você está passando hoje?\"", request.User);
        Assert.Contains("optionA", request.JsonSchema);
        Assert.Contains("\"additionalProperties\":false", request.JsonSchema);
    }

    [Theory]
    [InlineData(RewriteDirection.Shorter, "mais curta")]
    [InlineData(RewriteDirection.MoreNatural, "mais natural")]
    [InlineData(RewriteDirection.MoreFaithful, "mais fiel")]
    [InlineData(RewriteDirection.MoreFormal, "mais formal")]
    [InlineData(RewriteDirection.AsCharacter, "como o personagem falaria")]
    public async Task Cada_direcao_tem_a_propria_instrucao(RewriteDirection direcao, string trecho)
    {
        var client = new StubClient(_ => Ok("""{"rewrites":[{"id":"a","optionA":"A","optionB":"B"}]}"""));

        await new ClaudeTranslationProvider(client).RewriteAsync(new TranslationBatch([Unit("a", "x")]), new RewriteRequest(direcao, "texto"), CancellationToken.None);

        Assert.Contains(trecho, client.Requests[0].BatchContext);
        Assert.DoesNotContain("caixa do jogo", client.Requests[0].BatchContext);
    }

    [Fact]
    public async Task Recusa_marca_a_linha_como_recusada_sem_opcoes()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => new ClaudeReply("refusal", string.Empty)));

        var result = await provider.RewriteAsync(new TranslationBatch([Unit("a", "x")]), new RewriteRequest(RewriteDirection.Shorter, "texto longo", 3, 40), CancellationToken.None);

        Assert.True(result.Refused);
        Assert.Empty(result.Options);
    }

    [Fact]
    public async Task Resposta_sem_o_id_da_linha_e_recusada_como_json_invalido()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => Ok("""{"rewrites":[{"id":"outra","optionA":"A","optionB":"B"}]}""")));

        await Assert.ThrowsAsync<ClaudeResponseException>(() =>
            provider.RewriteAsync(new TranslationBatch([Unit("a", "x")]), new RewriteRequest(RewriteDirection.Shorter, "t"), CancellationToken.None));
    }

    [Fact]
    public async Task Opcao_vazia_some_da_lista()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => Ok("""{"rewrites":[{"id":"a","optionA":"Só esta","optionB":"  "}]}""")));

        var result = await provider.RewriteAsync(new TranslationBatch([Unit("a", "x")]), new RewriteRequest(RewriteDirection.MoreFormal, "t"), CancellationToken.None);

        Assert.Equal(["Só esta"], result.Options);
    }

    [Fact]
    public async Task Resposta_cortada_por_limite_de_tokens_explica_sem_falar_em_lote()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => new ClaudeReply("max_tokens", "{")));

        var erro = await Assert.ThrowsAsync<ClaudeResponseException>(() =>
            provider.RewriteAsync(new TranslationBatch([Unit("a", "x")]), new RewriteRequest(RewriteDirection.Shorter, "t"), CancellationToken.None));

        Assert.Contains("limite de tokens", erro.Message);
        Assert.DoesNotContain("lote", erro.Message);
    }

    [Fact]
    public async Task Mais_de_uma_linha_no_lote_e_recusado()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => Ok("{}")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.RewriteAsync(new TranslationBatch([Unit("a", "x"), Unit("b", "y")]), new RewriteRequest(RewriteDirection.Shorter, "t"), CancellationToken.None));
    }

    [Fact]
    public async Task Tokens_da_reescrita_entram_na_contagem()
    {
        var reply = new ClaudeReply("end_turn", """{"rewrites":[{"id":"a","optionA":"A","optionB":"B"}]}""", new ClaudeUsage(100, 20, 800, 50));
        var provider = new ClaudeTranslationProvider(new StubClient(_ => reply));

        await provider.RewriteAsync(new TranslationBatch([Unit("a", "x")]), new RewriteRequest(RewriteDirection.Shorter, "t"), CancellationToken.None);

        Assert.Equal(new ClaudeUsage(100, 20, 800, 50), provider.Usage);
    }

    [Fact]
    public async Task Provedor_falso_devolve_duas_opcoes_e_guarda_o_pedido()
    {
        var fake = new FakeTranslationProvider();
        var request = new RewriteRequest(RewriteDirection.MoreFormal, @"Oi\p");

        var result = await fake.RewriteAsync(new TranslationBatch([Unit("a", "x")]), request, CancellationToken.None);

        Assert.Equal(2, result.Options.Count);
        Assert.All(result.Options, o => Assert.EndsWith(@"Oi\p", o));
        Assert.Equal(request, Assert.Single(fake.ReceivedRewrites).Request);
    }

    private sealed class RewriteResultComparer : IEqualityComparer<RewriteResult>
    {
        public bool Equals(RewriteResult? x, RewriteResult? y) =>
            x is not null && y is not null && x.Id == y.Id && x.Refused == y.Refused && x.Options.SequenceEqual(y.Options);

        public int GetHashCode(RewriteResult obj) => obj.Id.GetHashCode();
    }
}
