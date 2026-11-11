using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using VNForge.AI.Bible;
using VNForge.AI.Claude;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Bible;

public sealed class ClaudeBibleProviderTests
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

    private static readonly string[] SceneCharacters = ["Kenta", "Ren"];
    private static readonly string[] SceneTone = ["cômico leve"];
    private static readonly string[] SceneJokes = ["o guarda-chuva"];

    private static string SceneJson(string key, string summary = "Kenta e Ren chegam à escola.") =>
        JsonSerializer.Serialize(new
        {
            key,
            name = "Atraso no portão",
            summary,
            characters = SceneCharacters,
            tone = SceneTone,
            jokes = SceneJokes,
            relations = new[] { new { between = "Kenta e Ren", note = "Amigos de infância" } },
        });

    private static ClaudeReply Scenes(params string[] keys) =>
        new("end_turn", $$"""{"scenes":[{{string.Join(",", keys.Select(k => SceneJson(k)))}}]}""", new ClaudeUsage(100, 50, 400, 0));

    private static BibleChapterInput TwoSceneChapter() =>
        Chapter("Manhã de chuva", 1, Scene("yst1", "manha", "おはよう", "遅いぞ"), Scene("yst2", "tarde", "ただいま"));

    [Fact]
    public async Task O_roteiro_do_capitulo_vai_no_bloco_estavel_e_o_pedido_de_cenas_no_do_usuario()
    {
        var client = new StubClient(_ => Scenes("yst1"));
        var provider = new ClaudeBibleProvider(client);

        await provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken);

        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-opus-5-5", request.Model);
        Assert.Contains("美咲: おはよう", request.System, StringComparison.Ordinal);
        Assert.Contains("健太: 遅いぞ", request.System, StringComparison.Ordinal);
        Assert.Contains("美咲 => Misaki", request.System, StringComparison.Ordinal);
        Assert.Contains("Capítulo: Manhã de chuva", request.System, StringComparison.Ordinal);
        Assert.Equal(string.Empty, request.BatchContext);
        Assert.Contains("yst1", request.User, StringComparison.Ordinal);
        Assert.DoesNotContain("おはよう", request.User, StringComparison.Ordinal);
        Assert.Contains("\"scenes\"", request.JsonSchema, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"scenes":[]}""")]
    [InlineData("""{"scenes":[{"key":"yst1","name":"a","summary":"b","characters":[],"tone":[],"jokes":[],"relations":[]},{"key":"yst9","name":"a","summary":"b","characters":[],"tone":[],"jokes":[],"relations":[]}]}""")]
    [InlineData("""{"scenes":[{"key":"yst1","name":"a","summary":"  ","characters":[],"tone":[],"jokes":[],"relations":[]}]}""")]
    [InlineData("""{"scenes":[{"key":"yst1"}]}""")]
    [InlineData("não é json")]
    public async Task Resposta_que_nao_bate_com_o_pedido_vira_erro_de_resposta(string json)
    {
        var provider = new ClaudeBibleProvider(new StubClient(_ => new ClaudeReply("end_turn", json)));

        var ex = await Assert.ThrowsAsync<ClaudeResponseException>(() => provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken));

        Assert.Contains("incompleta ou fora do formato", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("refusal", "")]
    [InlineData("max_tokens", "{")]
    public async Task Chamada_que_falha_ja_foi_cobrada_e_soma_no_uso(string stopReason, string text)
    {
        var provider = new ClaudeBibleProvider(new StubClient(_ => new ClaudeReply(stopReason, text, new ClaudeUsage(100, 50, 400, 20))));

        await Assert.ThrowsAnyAsync<Exception>(() => provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken));

        Assert.Equal(new ClaudeUsage(100, 50, 400, 20), provider.Usage);
    }

    [Fact]
    public async Task Pedir_uma_cena_que_nao_esta_no_capitulo_e_erro_de_uso()
    {
        var provider = new ClaudeBibleProvider(new StubClient(_ => Scenes("yst1")));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.DescribeScenesAsync(TwoSceneChapter(), ["yst77"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_sinopse_vem_dos_resumos_dos_capitulos()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"synopsis":"Uma cidade onde sempre chove.","timeline":[{"title":"Manhã de chuva","note":"O semestre começa"}]}"""));
        var provider = new ClaudeBibleProvider(client);

        var overview = await provider.SummarizeGameAsync([new BibleChapterDigest("Manhã de chuva", ["Kenta chega.", "Ren mente."])], TestContext.Current.CancellationToken);

        Assert.Equal("Uma cidade onde sempre chove.", overview.Synopsis);
        Assert.Equal("Manhã de chuva", Assert.Single(overview.Timeline).Title);
        var request = Assert.Single(client.Requests);
        Assert.Contains("Kenta chega.", request.User, StringComparison.Ordinal);
        Assert.Contains("\"timeline\"", request.JsonSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sinopse_vazia_e_resposta_incompleta()
    {
        var provider = new ClaudeBibleProvider(new StubClient(_ => new ClaudeReply("end_turn", """{"synopsis":" ","timeline":[]}""")));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => provider.SummarizeGameAsync([new BibleChapterDigest("A", ["x"])], TestContext.Current.CancellationToken));
    }

    private sealed class WireHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var text = JsonSerializer.Serialize("{\"scenes\":[" + SceneJson("yst1") + "]}");
            var reply = "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"m\",\"content\":[{\"type\":\"text\",\"text\":" + text + "}],\"stop_reason\":\"end_turn\",\"stop_sequence\":null,\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task No_fio_o_roteiro_do_capitulo_fica_no_bloco_de_sistema_com_cache_control()
    {
        var handler = new WireHandler();
        var sdk = new AnthropicClient { ApiKey = "chave-de-teste", MaxRetries = 0, HttpClient = new HttpClient(handler) };
        var provider = new ClaudeBibleProvider(new AnthropicMessageClient(sdk));

        await provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var system = body.RootElement.GetProperty("system").EnumerateArray().ToList();
        var block = Assert.Single(system);
        Assert.Equal("ephemeral", block.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Contains("美咲: おはよう", block.GetProperty("text").GetString(), StringComparison.Ordinal);
        var user = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.DoesNotContain("おはよう", user, StringComparison.Ordinal);
    }
}
