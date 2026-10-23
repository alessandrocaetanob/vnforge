using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class ClaudeTranslationProviderTests
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
    public void Modelo_padrao_e_opus_5_5_com_effort_medium()
    {
        var options = new ClaudeOptions();

        Assert.Equal("claude-opus-5-5", options.Model);
        Assert.Equal(ClaudeEffort.Medium, options.Effort);
    }

    [Fact]
    public void Ids_dos_modelos_suportados()
    {
        Assert.Equal(
            ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1"],
            ClaudeModels.All);
    }

    [Fact]
    public void Modelo_desconhecido_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeOptions { Model = "gpt-4" });
    }

    [Fact]
    public async Task Devolve_um_resultado_por_linha_na_ordem_do_lote()
    {
        // O modelo responde fora de ordem; o provedor reordena pelo lote.
        var client = new StubClient(_ => Ok("""{"translations":[{"id":"b","translation":"Tchau"},{"id":"a","translation":"Oi"}]}"""));
        var provider = new ClaudeTranslationProvider(client);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "やあ"), Unit("b", "さよなら")]), CancellationToken.None);

        Assert.Equal([new TranslationResult("a", "Oi"), new TranslationResult("b", "Tchau")], results);
    }

    [Fact]
    public async Task Requisicao_leva_modelo_effort_schema_json_e_as_linhas()
    {
        var client = new StubClient(_ => Ok("""{"translations":[{"id":"a","translation":"Oi"}]}"""));
        var provider = new ClaudeTranslationProvider(client, new ClaudeOptions { Model = ClaudeModels.Sonnet, Effort = ClaudeEffort.Low });
        var batch = new TranslationBatch([Unit("a", "やあ", "Rin")])
        {
            Glossary = [new GlossaryEntry("魔法", "magia")],
            Characters = [new CharacterInfo("凛", "Rin", "formal")],
            StyleGuide = "Tom leve.",
        };

        await provider.TranslateAsync(batch, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-sonnet-5-5", request.Model);
        Assert.Equal(ClaudeEffort.Low, request.Effort);
        Assert.Contains("\"translations\"", request.JsonSchema);
        Assert.Contains("Tom leve.", request.System);
        Assert.Contains("magia", request.System);
        Assert.Contains("formal", request.System);
        Assert.Contains("やあ", request.User);
        Assert.Contains("\"a\"", request.User);
    }

    [Fact]
    public async Task Recusa_de_uma_linha_marca_a_linha_sem_derrubar_o_lote()
    {
        // O lote inteiro é recusado; a linha "b" continua recusada sozinha, as outras passam.
        var client = new StubClient(r => r.User.Contains("\"id\":\"b\"") ? new ClaudeReply("refusal", string.Empty)
            : r.User.Contains("\"id\":\"a\"") && r.User.Contains("\"id\":\"c\"") ? new ClaudeReply("refusal", string.Empty)
            : r.User.Contains("\"id\":\"a\"") ? Ok("""{"translations":[{"id":"a","translation":"A"}]}""")
            : Ok("""{"translations":[{"id":"c","translation":"C"}]}"""));
        var provider = new ClaudeTranslationProvider(client);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "x"), Unit("b", "y"), Unit("c", "z")]), CancellationToken.None);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
        Assert.Equal(["A", string.Empty, "C"], results.Select(r => r.Translation));
        Assert.Equal([false, true, false], results.Select(r => r.Refused));
    }

    [Fact]
    public async Task Recusa_so_do_lote_e_resolvida_linha_a_linha()
    {
        var client = new StubClient(r => r.User.Contains("\"id\":\"a\"") && r.User.Contains("\"id\":\"b\"")
            ? new ClaudeReply("refusal", string.Empty)
            : r.User.Contains("\"id\":\"a\"") ? Ok("""{"translations":[{"id":"a","translation":"A"}]}""")
            : Ok("""{"translations":[{"id":"b","translation":"B"}]}"""));
        var provider = new ClaudeTranslationProvider(client);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "x"), Unit("b", "y")]), CancellationToken.None);

        Assert.Equal([new TranslationResult("a", "A"), new TranslationResult("b", "B")], results);
        Assert.All(results, r => Assert.False(r.Refused));
    }

    [Fact]
    public async Task Recusa_de_lote_de_uma_linha_marca_a_linha()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => new ClaudeReply("refusal", string.Empty)));

        var result = Assert.Single(await provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), CancellationToken.None));

        Assert.True(result.Refused);
        Assert.Equal(string.Empty, result.Translation);
    }

    [Fact]
    public async Task Tokens_lidos_do_cache_entram_na_contagem()
    {
        var reply = new ClaudeReply("end_turn", """{"translations":[{"id":"a","translation":"Oi"}]}""", new ClaudeUsage(100, 20, 800, 50));
        var provider = new ClaudeTranslationProvider(new StubClient(_ => reply));
        var batch = new TranslationBatch([Unit("a", "x")]);

        await provider.TranslateAsync(batch, CancellationToken.None);
        await provider.TranslateAsync(batch, CancellationToken.None);

        Assert.Equal(new ClaudeUsage(200, 40, 1600, 100), provider.Usage);
    }

    [Fact]
    public async Task Uso_da_recusa_tambem_e_contado()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => new ClaudeReply("refusal", string.Empty, new ClaudeUsage(10, 0, 5, 0))));

        await provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), CancellationToken.None);

        Assert.Equal(5, provider.Usage.CacheReadInputTokens);
    }

    [Fact]
    public void Fallback_de_recusa_so_existe_onde_o_servidor_oferece()
    {
        Assert.True(AnthropicMessageClient.SupportsServerFallback(ClaudeModels.Opus));
        Assert.True(AnthropicMessageClient.SupportsServerFallback(ClaudeModels.Fable));
        Assert.False(AnthropicMessageClient.SupportsServerFallback(ClaudeModels.Haiku));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"translations":[{"id":"zzz","translation":"x"}]}""")]
    [InlineData("""{"translations":[]}""")]
    [InlineData("""{"translations":[{"id":"a","translation":"x"},{"id":"a","translation":"y"}]}""")]
    public async Task Resposta_invalida_lanca_ClaudeResponseException(string json)
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => Ok(json)));

        await Assert.ThrowsAsync<ClaudeResponseException>(() =>
            provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelamento_e_respeitado_antes_da_chamada()
    {
        var client = new StubClient(_ => Ok("{}"));
        var provider = new ClaudeTranslationProvider(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), cts.Token));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Lote_vazio_nao_chama_a_API()
    {
        var client = new StubClient(_ => Ok("{}"));
        var provider = new ClaudeTranslationProvider(client);

        var results = await provider.TranslateAsync(new TranslationBatch([]), CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Integracao_real_so_roda_com_ANTHROPIC_API_KEY()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            return; // teste opcional: sem chave, não faz nada

        var provider = new ClaudeTranslationProvider(
            new AnthropicMessageClient(), new ClaudeOptions { Model = ClaudeModels.Haiku, Effort = ClaudeEffort.Low });

        var results = await provider.TranslateAsync(
            new TranslationBatch([Unit("a", "おはよう")]), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("a", result.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Translation));
    }
}
