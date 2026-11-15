using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

[Trait("Risco", "R5")]
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
        Assert.Contains("magia", request.BatchContext);
        Assert.Contains("formal", request.BatchContext);
        Assert.DoesNotContain("magia", request.System);
        Assert.Contains("やあ", request.User);
        Assert.Contains("\"a\"", request.User);
    }

    [Fact]
    public async Task Schema_pede_a_duvida_como_campo_opcional_com_os_cinco_tipos()
    {
        var client = new StubClient(_ => Ok("""{"translations":[{"id":"a","translation":"Oi"}]}"""));
        var provider = new ClaudeTranslationProvider(client);

        await provider.TranslateAsync(new TranslationBatch([Unit("a", "やあ")]), CancellationToken.None);

        using var schema = System.Text.Json.JsonDocument.Parse(Assert.Single(client.Requests).JsonSchema);
        var item = schema.RootElement.GetProperty("properties").GetProperty("translations").GetProperty("items");
        var doubt = item.GetProperty("properties").GetProperty("doubt");
        Assert.Equal(["id", "translation"], item.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            ["pun", "hidden-subject", "uncertain-gender", "cultural-reference", "tone"],
            doubt.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["kind", "note"], doubt.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("dúvida", client.Requests[0].System);
    }

    [Fact]
    public async Task Uso_da_recusa_tambem_e_contado()
    {
        var provider = new ClaudeTranslationProvider(new StubClient(_ => new ClaudeReply("refusal", string.Empty, new ClaudeUsage(10, 0, 5, 0))));

        await provider.TranslateAsync(new TranslationBatch([Unit("a", "x")]), CancellationToken.None);

        Assert.Equal(5, provider.Usage.CacheReadInputTokens);
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
}
