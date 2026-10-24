using System.Net;
using Anthropic.Exceptions;
using VNForge.AI.Claude;

namespace VNForge.AI.Tests;

public class ClaudeConnectionTestTests
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

    private sealed class ThrowingClient(Exception error) : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) => throw error;
    }

    [Fact]
    public async Task Resposta_da_api_conta_como_conexao_boa_e_o_pedido_e_o_menor_possivel()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", "{\"ok\":true}", new ClaudeUsage(20, 8, 0, 0)));

        var result = await ClaudeConnectionTest.RunAsync(client, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ClaudeModels.Haiku, result.Model);
        Assert.Contains("Claude Haiku 5.5", result.Message);
        var request = Assert.Single(client.Requests);
        Assert.Equal(ClaudeModels.Haiku, request.Model);
        Assert.Equal(ClaudeEffort.Low, request.Effort);
        Assert.True(request.MaxTokens <= 256, "o teste não deve gastar mais que alguns tokens");
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    public async Task Qualquer_resposta_prova_que_a_chave_vale_mesmo_sem_o_texto_esperado(string stopReason)
    {
        var client = new StubClient(_ => new ClaudeReply(stopReason, string.Empty));

        var result = await ClaudeConnectionTest.RunAsync(client, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Chave_recusada_vira_mensagem_em_portugues()
    {
        var rede = new HttpRequestException("401");
        var client = new ThrowingClient(new AnthropicUnauthorizedException(rede) { StatusCode = HttpStatusCode.Unauthorized, ResponseBody = "sk-ant-segredo" });

        var result = await ClaudeConnectionTest.RunAsync(client, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("A chave de API foi recusada. Confira a chave em Ajustes.", result.Message);
        Assert.DoesNotContain("segredo", result.Message);
    }

    [Fact]
    public async Task Falha_de_rede_vira_mensagem_em_portugues()
    {
        var result = await ClaudeConnectionTest.RunAsync(new ThrowingClient(new HttpRequestException("sem rede")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rede", result.Message);
    }

    [Fact]
    public async Task Cancelar_o_teste_propaga_o_cancelamento()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new ThrowingClient(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClaudeConnectionTest.RunAsync(client, cts.Token));
    }
}
