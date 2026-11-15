using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Vndb;

namespace VNForge.Vndb.Tests;

[Trait("Risco", "R5")]
[Trait("Risco", "R7")]
public class VndbClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// <paramref name="time"/> é o relógio da fila. O relógio do prazo do cliente (<paramref name="clientTime"/>) é o do sistema, de propósito: os testes que andam
    /// <paramref name="time"/> aos saltos não podem estourar o prazo de um pedido que esteja no ar.
    /// </summary>
    private static VndbClient Client(
        StubHandler handler,
        FakeTimeProvider? time = null,
        VndbCache? cache = null,
        int maxRequests = VndbRequestQueue.DefaultMaxRequests,
        VndbRequestQueue? queue = null,
        TimeProvider? clientTime = null,
        TimeSpan? timeout = null)
    {
        time ??= new FakeTimeProvider();
        return new VndbClient(new HttpClient(handler), queue ?? new VndbRequestQueue(time, maxRequests), cache, time: clientTime, timeout: timeout);
    }

    private static JsonNode Body(SentRequest request) => JsonNode.Parse(request.Body)!;

    /// <summary>
    /// Anda o relógio de teste até a tarefa terminar. Anda aos poucos porque o cliente só para a fila depois de ler o 429; adiantar o relógio antes disso
    /// não solta nada, e o próximo passo solta.
    /// </summary>
    private static async Task AdvanceUntilDone(Task task, FakeTimeProvider time, TimeSpan step)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++)
        {
            time.Advance(step);
            await Task.Delay(1);
        }

        Assert.True(task.IsCompleted, "A tarefa não terminou mesmo andando o relógio.");
    }

    [Fact]
    public async Task Resposta_ilegivel_nao_vai_para_o_cache()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var handler = new StubHandler((_, n) => StubHandler.Json(n == 1 ? "isto nao e json" : VndbSamples.VisualNovels));
        var client = Client(handler, time, new VndbCache(dir.Path, time));

        await Assert.ThrowsAsync<VndbException>(() => client.SearchVisualNovelsAsync("Moon Flower", ct: Ct));
        var found = await client.SearchVisualNovelsAsync("Moon Flower", ct: Ct);

        Assert.Single(found);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Quatro_respostas_429_seguidas_viram_erro_em_portugues()
    {
        var time = new FakeTimeProvider();
        var handler = new StubHandler((_, _) => StubHandler.Text(HttpStatusCode.TooManyRequests, "Throttled"));
        var pending = Client(handler, time).SearchVisualNovelsAsync("x", ct: Ct);

        await AdvanceUntilDone(pending, time, TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<VndbException>(() => pending);

        Assert.Equal(429, error.StatusCode);
        Assert.Contains("limitou", error.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Falha_de_rede_vira_VndbException()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("sem rede"));

        var error = await Assert.ThrowsAsync<VndbException>(() => Client(handler).SearchVisualNovelsAsync("x", ct: Ct));

        Assert.Contains("Não foi possível falar com o VNDB", error.Message);
        Assert.Null(error.StatusCode);
    }

    [Fact]
    public async Task Lista_sem_token_nao_manda_cabecalho_de_autorizacao()
    {
        var handler = new StubHandler(VndbSamples.ListSecondPage);

        await Client(handler).GetListAsync("u2", ct: Ct);

        Assert.Null(handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task Token_com_espaco_no_meio_e_recusado_sem_pedido()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Assert.ThrowsAsync<VndbException>(() => Client(handler).UpdateEntryAsync("abc def", "v17", new VndbListUpdate(), ct: Ct));

        Assert.Empty(handler.Requests);
    }
}
