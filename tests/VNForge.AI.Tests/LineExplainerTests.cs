using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Explain;
using VNForge.Core.Projects;

namespace VNForge.AI.Tests;

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
    public async Task Quebra_o_original_em_partes_com_o_significado_de_cada_uma()
    {
        var client = new StubClient(_ => Ok(Reply));

        var explicacao = await new LineExplainer(client, Cache).ExplainAsync(Original, CancellationToken.None);

        Assert.Equal(
            [new ExplainedPart("今日は", "hoje (tema da frase)"), new ExplainedPart("雨が", "a chuva (sujeito)"), new ExplainedPart("降っている", "está caindo")],
            explicacao.Parts);
        Assert.False(explicacao.FromCache);
        Assert.Equal("claude-haiku-5-5", explicacao.Model);
    }

    [Fact]
    public async Task O_pedido_vai_ao_Haiku_com_o_schema_de_partes_e_o_original_sem_marcadores()
    {
        var client = new StubClient(_ => Ok(Reply));

        await new LineExplainer(client, Cache).ExplainAsync("今日は\\n雨が降っている。\\p", CancellationToken.None);

        var pedido = Assert.Single(client.Requests);
        Assert.Equal(ClaudeModels.Haiku, pedido.Model);
        Assert.Equal(ClaudeEffort.Low, pedido.Effort);
        Assert.Equal(LineExplainer.Schema, pedido.JsonSchema);
        Assert.Contains("今日は雨が降っている。", pedido.User, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\\p", pedido.User, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\\n", pedido.User, StringComparison.Ordinal);
        Assert.Contains("português do Brasil", pedido.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mesma_fala_vem_do_cache_sem_chamar_a_API()
    {
        var client = new StubClient(_ => Ok(Reply));
        var explicador = new LineExplainer(client, Cache);

        await explicador.ExplainAsync(Original, CancellationToken.None);
        var segunda = await explicador.ExplainAsync(Original, CancellationToken.None);

        Assert.Single(client.Requests);
        Assert.True(segunda.FromCache);
        Assert.Equal(3, segunda.Parts.Count);
    }

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

    [Fact]
    public async Task Originais_diferentes_nao_dividem_o_cache()
    {
        var client = new StubClient(_ => Ok(Reply));
        var explicador = new LineExplainer(client, Cache);

        await explicador.ExplainAsync(Original, CancellationToken.None);
        await explicador.ExplainAsync("今日は雨が降っている。", CancellationToken.None);

        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Recusa_do_modelo_avisa_em_portugues_e_nao_entra_no_cache()
    {
        var recusar = true;
        var client = new StubClient(_ => recusar ? new ClaudeReply("refusal", string.Empty, new ClaudeUsage(500, 10, 0, 0)) : Ok(Reply));
        var explicador = new LineExplainer(client, Cache);

        var erro = await Assert.ThrowsAsync<ClaudeRefusalException>(() => explicador.ExplainAsync(Original, CancellationToken.None));
        Assert.Contains("recusou", erro.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Cache));

        recusar = false;
        var explicacao = await explicador.ExplainAsync(Original, CancellationToken.None);

        Assert.False(explicacao.FromCache);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Resposta_cortada_por_limite_de_tokens_e_erro()
    {
        var client = new StubClient(_ => new ClaudeReply("max_tokens", """{"parts":[{"source":"今日は"""));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => new LineExplainer(client, Cache).ExplainAsync(Original, CancellationToken.None));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"parts":[]}""")]
    [InlineData("""{"parts":[{"source":"","meaning":""}]}""")]
    [InlineData("""{"outra":1}""")]
    public async Task Resposta_fora_do_formato_nao_vira_explicacao_nem_cache(string texto)
    {
        var client = new StubClient(_ => Ok(texto));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => new LineExplainer(client, Cache).ExplainAsync(Original, CancellationToken.None));
        Assert.False(File.Exists(Cache));
    }

    [Fact]
    public async Task Fala_so_de_marcadores_nao_chama_a_API()
    {
        var client = new StubClient(_ => Ok(Reply));

        var explicacao = await new LineExplainer(client, Cache).ExplainAsync("\\p", CancellationToken.None);

        Assert.Empty(explicacao.Parts);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Cache_corrompido_e_refeito_sem_derrubar_o_explicador()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
        await File.WriteAllTextAsync(Cache, "{ isto não é json", TestContext.Current.CancellationToken);
        var client = new StubClient(_ => Ok(Reply));

        var explicacao = await new LineExplainer(client, Cache).ExplainAsync(Original, CancellationToken.None);

        Assert.Equal(3, explicacao.Parts.Count);
        Assert.Contains(SourceHash.Of(Original), await File.ReadAllTextAsync(Cache, TestContext.Current.CancellationToken), StringComparison.Ordinal);
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

    [Fact]
    public void O_preco_estimado_de_uma_explicacao_e_uma_fracao_de_centavo_em_reais()
    {
        var reais = new ExchangeRate(5.40m).ToBrl(LineExplainer.EstimateDollars());

        Assert.InRange(reais, 0.0001m, 0.01m);
    }
}
