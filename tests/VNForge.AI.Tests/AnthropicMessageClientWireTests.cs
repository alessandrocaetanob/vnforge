using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using VNForge.AI.Claude;

namespace VNForge.AI.Tests;

/// <summary>Passa pelo SDK de verdade e inspeciona o pedido HTTP, que os provedores falsos não enxergam.</summary>
public sealed class AnthropicMessageClientWireTests
{
    private const string Ok = """
        {"id":"msg_1","type":"message","role":"assistant","model":"m","content":[{"type":"text","text":"{}"}],
         "stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}
        """;

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Ok, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static async Task<(JsonElement Body, HttpRequestMessage Request)> CaptureAsync(string model, string batchContext = "")
    {
        var handler = new CapturingHandler();
        var sdk = new AnthropicClient
        {
            ApiKey = "chave-de-teste",
            MaxRetries = 0,
            HttpClient = new HttpClient(handler),
        };
        var request = new ClaudeRequest(model, ClaudeEffort.Low, 1024, "sistema", "usuario", """{"type":"object"}""", batchContext);

        await new AnthropicMessageClient(sdk).CompleteAsync(request, CancellationToken.None);

        var body = handler.Body ?? throw new InvalidOperationException("O SDK não enviou corpo.");
        var sent = handler.Request ?? throw new InvalidOperationException("O SDK não enviou pedido.");
        return (JsonDocument.Parse(body).RootElement.Clone(), sent);
    }

    private static bool HasBeta(HttpRequestMessage request) =>
        request.Headers.TryGetValues("anthropic-beta", out var values)
        && values.Any(v => v.Contains("server-side-fallback", StringComparison.Ordinal));

    [Fact]
    public async Task O_sistema_vai_em_dois_blocos_e_so_o_primeiro_leva_cache_control()
    {
        var (body, _) = await CaptureAsync(ClaudeModels.Opus, "glossário do lote");

        var system = body.GetProperty("system").EnumerateArray().ToList();
        Assert.Equal(2, system.Count);
        Assert.Equal("sistema", system[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", system[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("glossário do lote", system[1].GetProperty("text").GetString());
        Assert.False(system[1].TryGetProperty("cache_control", out _), system[1].ToString());
    }

    [Fact]
    public async Task Sem_contexto_do_lote_o_sistema_e_um_bloco_so()
    {
        var (body, _) = await CaptureAsync(ClaudeModels.Opus);

        Assert.Single(body.GetProperty("system").EnumerateArray());
    }

    [Fact]
    public async Task Haiku_nao_manda_fallbacks_nem_header_beta_de_fallback()
    {
        var (body, request) = await CaptureAsync(ClaudeModels.Haiku);

        Assert.False(body.TryGetProperty("fallbacks", out _), body.ToString());
        Assert.False(HasBeta(request));
    }

    [Theory]
    [InlineData(ClaudeModels.Opus)]
    [InlineData(ClaudeModels.Fable)]
    public async Task Modelos_com_fallback_mandam_a_lista_e_o_header_beta(string model)
    {
        var (body, request) = await CaptureAsync(model);

        var fallbacks = body.GetProperty("fallbacks");
        Assert.Equal("claude-opus-4-8", Assert.Single(fallbacks.EnumerateArray()).GetProperty("model").GetString());
        Assert.True(HasBeta(request));
    }
}
