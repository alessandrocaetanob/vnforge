using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using VNForge.AI.Claude;

namespace VNForge.AI.Tests;

/// <summary>Passa pelo SDK de verdade e inspeciona o pedido HTTP, que os provedores falsos não enxergam.</summary>
[Trait("Risco", "R5")]
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

    private static async Task<(JsonElement Body, HttpRequestMessage Request)> CaptureAsync(string model, string batchContext = "", ClaudeImage? image = null)
    {
        var handler = new CapturingHandler();
        var sdk = new AnthropicClient
        {
            ApiKey = "chave-de-teste",
            MaxRetries = 0,
            HttpClient = new HttpClient(handler),
        };
        var request = new ClaudeRequest(model, ClaudeEffort.Low, 1024, "sistema", "usuario", """{"type":"object"}""", batchContext, image);

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
    public async Task Imagem_vai_como_bloco_base64_antes_do_texto_do_usuario()
    {
        byte[] pixels = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        var (body, _) = await CaptureAsync(ClaudeModels.Haiku, image: new ClaudeImage("image/png", pixels));

        var content = body.GetProperty("messages")[0].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, content.Count);
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        var source = content[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal(pixels, Convert.FromBase64String(source.GetProperty("data").GetString()!));
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Equal("usuario", content[1].GetProperty("text").GetString());
    }
}
