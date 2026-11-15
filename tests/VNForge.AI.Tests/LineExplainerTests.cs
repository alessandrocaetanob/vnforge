using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Explain;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.AI.Tests;

[Trait("Risco", "R5")]
[Trait("Risco", "R7")]
public sealed class LineExplainerTests : IDisposable
{
    private const string Original = "今日は雨が降っている。\\p";
    private const string Reply = """{"parts":[{"source":"今日は","meaning":"hoje (tema da frase)"},{"source":"雨が","meaning":"a chuva (sujeito)"},{"source":"降っている","meaning":"está caindo"}]}""";

    private readonly string pasta = Path.Combine(Path.GetTempPath(), "vnforge-explain-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(pasta))
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    private string Cache => Path.Combine(pasta, ".cache", "explain.json");

    private sealed class StubClient(Func<ClaudeRequest, ClaudeReply> reply) : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(reply(request));
        }
    }

    private static ClaudeReply Ok(string json) => new("end_turn", json, new ClaudeUsage(500, 300, 0, 0));

    [Fact]
    public async Task O_cache_fica_em_arquivo_pelo_hash_do_original_e_sobrevive_a_um_novo_explicador()
    {
        var primeiro = new StubClient(_ => Ok(Reply));
        await new LineExplainer(primeiro, Cache).ExplainAsync(Original, CancellationToken.None);

        Assert.Contains(SourceHash.Of(Original), await File.ReadAllTextAsync(Cache, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        var segundo = new StubClient(_ => throw new InvalidOperationException("não devia chamar a API"));
        var explicacao = await new LineExplainer(segundo, Cache).ExplainAsync(Original, CancellationToken.None);

        Assert.Empty(segundo.Requests);
        Assert.True(explicacao.FromCache);
        Assert.Equal("今日は", explicacao.Parts[0].Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ isto não é json")]
    public void R7_cache_vazio_ou_corrompido_recusa_em_portugues_e_nao_apaga_as_explicacoes(string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
        File.WriteAllText(Cache, conteudo);

        var erro = Assert.Throws<InvalidFileException>(() => new LineExplainer(new StubClient(_ => Ok(Reply)), Cache));

        Assert.Contains("apague", erro.Message);
        Assert.Equal(conteudo, File.ReadAllText(Cache));
    }

    [Fact]
    public async Task Os_tokens_gastos_somam_e_o_cache_nao_gasta()
    {
        var client = new StubClient(_ => Ok(Reply));
        var explicador = new LineExplainer(client, Cache);

        await explicador.ExplainAsync(Original, CancellationToken.None);
        await explicador.ExplainAsync(Original, CancellationToken.None);

        Assert.Equal(new ClaudeUsage(500, 300, 0, 0), explicador.Usage);
    }
}
