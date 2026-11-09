using VNForge.AI.Claude;
using VNForge.AI.Images;
using VNForge.Core.Images;

namespace VNForge.AI.Tests.Images;

public sealed class ClaudeImageTextReaderTests
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

    private static readonly ImageToRead Image = new("ui.ypf/menu/titulo.png", "image/png", [1, 2, 3, 4, 5]);

    private static ClaudeReply Ok(string json, ClaudeUsage? usage = null) => new("end_turn", json, usage);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task O_pedido_vai_para_o_haiku_com_a_imagem_em_base64_e_o_schema_de_texto()
    {
        var client = new StubClient(_ => Ok("""{"hasText":true,"text":"はじめから"}"""));

        await new ClaudeImageTextReader(client).ReadAsync(Image, Token);

        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-haiku-5-5", request.Model);
        Assert.Equal("image/png", request.Image?.MediaType);
        Assert.Equal(Image.Data, request.Image?.Data);
        Assert.Contains("ui.ypf/menu/titulo.png", request.User);
        Assert.Contains("\"hasText\"", request.JsonSchema);
        Assert.Contains("japonês", request.System);
    }

    [Fact]
    public async Task Devolve_o_texto_lido()
    {
        var client = new StubClient(_ => Ok("""{"hasText":true,"text":" はじめから\nつづきから "}"""));

        var reading = await new ClaudeImageTextReader(client).ReadAsync(Image, Token);

        Assert.Equal("はじめから\nつづきから", reading.Text);
        Assert.False(reading.Refused);
    }

    [Theory]
    [InlineData("""{"hasText":false,"text":""}""")]
    [InlineData("""{"hasText":false,"text":"ruído que o modelo não devia mandar"}""")]
    [InlineData("""{"hasText":true,"text":"   "}""")]
    public async Task Sem_texto_o_resultado_e_vazio(string json)
    {
        var reading = await new ClaudeImageTextReader(new StubClient(_ => Ok(json))).ReadAsync(Image, Token);

        Assert.Equal(string.Empty, reading.Text);
        Assert.False(reading.Refused);
    }

    [Fact]
    public async Task Recusa_do_modelo_vira_leitura_recusada()
    {
        var client = new StubClient(_ => new ClaudeReply("refusal", string.Empty));

        var reading = await new ClaudeImageTextReader(client).ReadAsync(Image, Token);

        Assert.True(reading.Refused);
    }

    [Fact]
    public async Task Resposta_cortada_pelo_limite_de_tokens_e_erro()
    {
        var client = new StubClient(_ => new ClaudeReply("max_tokens", """{"hasText":true,"te"""));

        var error = await Assert.ThrowsAsync<ClaudeResponseException>(() => new ClaudeImageTextReader(client).ReadAsync(Image, Token));

        Assert.Contains("max_tokens", error.Detail);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("""{"hasText":true}""")]
    [InlineData("""{"hasText":"sim","text":"x"}""")]
    public async Task Resposta_fora_do_formato_e_erro_em_portugues(string json)
    {
        var error = await Assert.ThrowsAsync<ClaudeResponseException>(() => new ClaudeImageTextReader(new StubClient(_ => Ok(json))).ReadAsync(Image, Token));

        Assert.Contains("fora do formato", error.Message);
    }

    [Fact]
    public async Task Conta_os_tokens_da_chamada_com_o_cache_incluido()
    {
        var client = new StubClient(_ => Ok("""{"hasText":false,"text":""}""", new ClaudeUsage(1000, 40, 200, 30)));

        var reading = await new ClaudeImageTextReader(client).ReadAsync(Image, Token);

        Assert.Equal(1230, reading.InputTokens);
        Assert.Equal(40, reading.OutputTokens);
    }
}
