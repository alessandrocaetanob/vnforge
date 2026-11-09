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

    private static string SceneJson(string key, string summary = "Kenta e Ren chegam à escola.") =>
        JsonSerializer.Serialize(new
        {
            key,
            name = "Atraso no portão",
            summary,
            characters = new[] { "Kenta", "Ren" },
            tone = new[] { "cômico leve" },
            jokes = new[] { "o guarda-chuva" },
            relations = new[] { new { between = "Kenta e Ren", note = "Amigos de infância" } },
        });

    private static ClaudeReply Scenes(params string[] keys) =>
        new("end_turn", $$"""{"scenes":[{{string.Join(",", keys.Select(k => SceneJson(k)))}}]}""", new ClaudeUsage(100, 50, 400, 0));

    private static BibleChapterInput TwoSceneChapter() =>
        Chapter("Manhã de chuva", 1, Scene("yst1", "manha", "おはよう", "遅いぞ"), Scene("yst2", "tarde", "ただいま"));

    [Fact]
    public async Task Descreve_as_cenas_pedidas_na_ordem_pedida()
    {
        var client = new StubClient(_ => Scenes("yst2", "yst1"));
        var provider = new ClaudeBibleProvider(client);

        var scenes = await provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1", "yst2"], TestContext.Current.CancellationToken);

        Assert.Equal(["yst1", "yst2"], scenes.Select(s => s.Key));
        var first = scenes[0];
        Assert.Equal("Atraso no portão", first.Name);
        Assert.Equal("Kenta e Ren chegam à escola.", first.Summary);
        Assert.Equal(["Kenta", "Ren"], first.Characters);
        Assert.Equal(["cômico leve"], first.Tone);
        Assert.Equal(["o guarda-chuva"], first.Jokes);
        Assert.Equal(new VNForge.Core.Bible.BibleRelation("Kenta e Ren", "Amigos de infância"), Assert.Single(first.Relations));
        Assert.Empty(first.Edited);
    }

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

    [Fact]
    public async Task Chamadas_sobre_o_mesmo_capitulo_mandam_o_mesmo_bloco_estavel_para_o_cache_acertar()
    {
        var client = new StubClient(r => r.User.Contains("yst2", StringComparison.Ordinal) ? Scenes("yst2") : Scenes("yst1"));
        var provider = new ClaudeBibleProvider(client);
        var chapter = TwoSceneChapter();

        await provider.DescribeScenesAsync(chapter, ["yst1"], TestContext.Current.CancellationToken);
        await provider.DescribeScenesAsync(chapter, ["yst2"], TestContext.Current.CancellationToken);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(client.Requests[0].System, client.Requests[1].System);
        Assert.NotEqual(client.Requests[0].User, client.Requests[1].User);
    }

    [Fact]
    public async Task Soma_o_uso_de_todas_as_chamadas_com_o_que_veio_do_cache()
    {
        var client = new StubClient(_ => Scenes("yst1"));
        var provider = new ClaudeBibleProvider(client);

        await provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken);
        await provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken);

        Assert.Equal(new ClaudeUsage(200, 100, 800, 0), provider.Usage);
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

    [Fact]
    public async Task Recusa_e_corte_por_tamanho_viram_erros_com_mensagem_em_portugues()
    {
        var refusal = new ClaudeBibleProvider(new StubClient(_ => new ClaudeReply("refusal", "")));
        var truncated = new ClaudeBibleProvider(new StubClient(_ => new ClaudeReply("max_tokens", "{")));

        var refused = await Assert.ThrowsAsync<ClaudeRefusalException>(() => refusal.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken));
        var cut = await Assert.ThrowsAsync<ClaudeResponseException>(() => truncated.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken));

        Assert.Contains("não quis ler", refused.Message, StringComparison.Ordinal);
        Assert.Contains("não terminou", cut.Message, StringComparison.Ordinal);
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
