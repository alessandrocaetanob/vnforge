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
    public async Task Recusa_do_modelo_vira_ClaudeRefusalException()
    {
        var client = new StubClient(_ => new ClaudeReply("refusal", string.Empty));
        var provider = new ClaudeTranslationProvider(client);

        await Assert.ThrowsAsync<ClaudeRefusalException>(() =>
            provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), CancellationToken.None));
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
