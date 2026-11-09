using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using VNForge.AI.Batches;
using VNForge.AI.Claude;

namespace VNForge.AI.Tests.Batches;

/// <summary>Passa pelo SDK de verdade e inspeciona o pedido HTTP e a leitura da resposta da Message Batches API.</summary>
public sealed class AnthropicMessageBatchClientWireTests
{
    private const string Batch = """
        {"id":"msgbatch_01","type":"message_batch","processing_status":"{status}","request_counts":{"processing":{processing},"succeeded":{succeeded},"errored":0,"canceled":0,"expired":0},
         "ended_at":null,"created_at":"2026-11-08T23:41:00Z","expires_at":"2026-11-09T23:41:00Z","cancel_initiated_at":null,"results_url":null,"archived_at":null}
        """;

    private sealed class Handler(Func<HttpRequestMessage, string, (string Body, string Type)> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            var (reply, type) = answer(request, body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, type) };
        }
    }

    private static AnthropicClient Sdk(Handler handler) => new() { ApiKey = "chave-de-teste", MaxRetries = 0, HttpClient = new HttpClient(handler) };

    private static string BatchJson(string status, int processing, int succeeded) =>
        Batch.Replace("{status}", status, StringComparison.Ordinal).Replace("{processing}", processing.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{succeeded}", succeeded.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    [Fact]
    public async Task Envia_cada_pedido_com_custom_id_cache_control_effort_e_schema()
    {
        var handler = new Handler((_, _) => (BatchJson("in_progress", 1, 0), "application/json"));
        var request = new ClaudeRequest(ClaudeModels.Opus, ClaudeEffort.Medium, 4096, "sistema", "usuario", """{"type":"object"}""", "glossário do lote");

        var info = await new AnthropicMessageBatchClient(Sdk(handler)).CreateAsync([new MessageBatchItem("r00001", request)], TestContext.Current.CancellationToken);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/v1/messages/batches", call.Path);
        var sent = JsonDocument.Parse(call.Body).RootElement.GetProperty("requests")[0];
        Assert.Equal("r00001", sent.GetProperty("custom_id").GetString());
        var parameters = sent.GetProperty("params");
        Assert.Equal(ClaudeModels.Opus, parameters.GetProperty("model").GetString());
        Assert.Equal(4096, parameters.GetProperty("max_tokens").GetInt32());
        Assert.Equal("adaptive", parameters.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("medium", parameters.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("object", parameters.GetProperty("output_config").GetProperty("format").GetProperty("schema").GetProperty("type").GetString());
        var system = parameters.GetProperty("system").EnumerateArray().ToList();
        Assert.Equal("ephemeral", system[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.False(system[1].TryGetProperty("cache_control", out _));
        Assert.Equal("usuario", parameters.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("msgbatch_01", info.Id);
        Assert.Equal(MessageBatchStatus.InProgress, info.Status);
    }

    [Fact]
    public async Task Le_o_estado_e_as_contagens_do_lote()
    {
        var handler = new Handler((_, _) => (BatchJson("ended", 0, 7), "application/json"));

        var info = await new AnthropicMessageBatchClient(Sdk(handler)).GetAsync("msgbatch_01", TestContext.Current.CancellationToken);

        Assert.Equal("/v1/messages/batches/msgbatch_01", handler.Calls[0].Path);
        Assert.Equal(MessageBatchStatus.Ended, info.Status);
        Assert.Equal(7, info.Succeeded);
        Assert.Equal(7, info.Done);
        Assert.Equal(1.0, info.Fraction);
    }

    [Fact]
    public async Task Cancelar_chama_o_endpoint_de_cancelamento()
    {
        var handler = new Handler((_, _) => (BatchJson("canceling", 3, 0), "application/json"));

        var info = await new AnthropicMessageBatchClient(Sdk(handler)).CancelAsync("msgbatch_01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Calls[0].Method);
        Assert.Equal("/v1/messages/batches/msgbatch_01/cancel", handler.Calls[0].Path);
        Assert.Equal(MessageBatchStatus.Canceling, info.Status);
        Assert.Equal(0.0, info.Fraction);
    }

    [Fact]
    public async Task Le_os_resultados_em_jsonl_pelo_custom_id_com_sucesso_erro_e_expirado()
    {
        const string Message = """{"id":"msg_1","type":"message","role":"assistant","model":"m","content":[{"type":"text","text":"{\"translations\":[]}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":5,"cache_read_input_tokens":3,"cache_creation_input_tokens":0}}""";
        var jsonl = string.Join(
            "\n",
            """{"custom_id":"r00001","result":{"type":"succeeded","message":""" + Message + "}}",
            """{"custom_id":"r00002","result":{"type":"errored","error":{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}}}""",
            """{"custom_id":"r00003","result":{"type":"expired"}}""",
            """{"custom_id":"r00004","result":{"type":"canceled"}}""");
        var handler = new Handler((_, _) => (jsonl, "application/x-jsonl"));

        var entries = new List<MessageBatchEntry>();
        await foreach (var entry in new AnthropicMessageBatchClient(Sdk(handler)).ResultsAsync("msgbatch_01", TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        Assert.Equal("/v1/messages/batches/msgbatch_01/results", handler.Calls[0].Path);
        Assert.Equal(["r00001", "r00002", "r00003", "r00004"], entries.Select(e => e.CustomId));
        Assert.Equal([MessageBatchOutcome.Succeeded, MessageBatchOutcome.Errored, MessageBatchOutcome.Expired, MessageBatchOutcome.Canceled], entries.Select(e => e.Outcome));
        var reply = entries[0].Reply!;
        Assert.Equal("end_turn", reply.StopReason);
        Assert.Equal("""{"translations":[]}""", reply.Text);
        Assert.Equal(12, reply.Usage!.InputTokens);
        Assert.Equal(5, reply.Usage.OutputTokens);
        Assert.Equal(3, reply.Usage.CacheReadInputTokens);
        Assert.Contains("overloaded_error", entries[1].Error, StringComparison.Ordinal);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"type":"error","error":{"type":"x","message":"y"}}""", Encoding.UTF8, "application/json") });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task Erro_permanente_e_o_que_repetir_nao_resolve(HttpStatusCode status, bool permanent)
    {
        var sdk = new AnthropicClient { ApiKey = "chave-de-teste", MaxRetries = 0, HttpClient = new HttpClient(new StatusHandler(status)) };

        var error = await Assert.ThrowsAnyAsync<Exception>(() => new AnthropicMessageBatchClient(sdk).GetAsync("msgbatch_01", TestContext.Current.CancellationToken));

        Assert.Equal(permanent, ClaudeErrorMessages.IsPermanent(error));
    }
}
