using VNForge.AI.Claude;
using VNForge.AI.Images;
using VNForge.Core.Images;

namespace VNForge.AI.Tests.Images;

[Trait("Risco", "R5")]
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
    public async Task Recusa_do_modelo_vira_leitura_recusada()
    {
        var client = new StubClient(_ => new ClaudeReply("refusal", string.Empty));

        var reading = await new ClaudeImageTextReader(client).ReadAsync(Image, Token);

        Assert.True(reading.Refused);
    }
}
