using VNForge.AI.Claude;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class LiveProgressTests
{
    private const string Descricao = "Trabalhando";

    [Fact]
    public void Tudo_pronto_nao_falta_tempo()
    {
        Assert.Equal(TimeSpan.Zero, BatchProgressText.Remaining(TimeSpan.FromSeconds(30), done: 100, total: 100));
    }

    [Theory]
    [InlineData(0.2, "1 s")]
    [InlineData(3900, "1 h 05 min")]
    public void Tempo_restante_arredonda_para_cima(double seconds, string expected)
    {
        Assert.Equal(expected, BatchProgressText.FormatRemaining(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0, 0)]
    public void Lotes_sao_as_linhas_divididas_pelo_tamanho_do_lote_arredondando_para_cima(int lines, int batches)
    {
        Assert.Equal(batches, BatchProgressText.BatchCount(lines, 30));
    }

    [Fact]
    public void Linha_de_andamento_traz_lote_linhas_tokens_custo_e_tempo_restante()
    {
        var usage = new ClaudeUsage(InputTokens: 15_000, OutputTokens: 3_430, CacheReadInputTokens: 0, CacheCreationInputTokens: 0);

        var text = BatchProgressText.Detail(7, 12, 210, 360, usage, 0.84m, TimeSpan.FromSeconds(100));

        Assert.Equal("lote 7/12  210/360 linhas  18.430 tokens  US$ 0,84  ~2 min", text);
    }

    [Fact]
    public async Task Em_terminal_interativo_a_barra_aparece_e_o_trabalho_devolve_o_resultado()
    {
        var harness = ConsoleHarness.For(TestTerminals.InteractivePlain());
        var live = false;

        var result = await LiveProgress.RunAsync(harness.Console, live: true, Descricao, total: null, sink =>
        {
            live = sink.IsLive;
            sink.Scripts(new ScriptWorkProgress(0, 2, "yst00001.ybn"), "fim");
            sink.Scripts(new ScriptWorkProgress(1, 2, "yst00002.ybn"), "fim");
            sink.Scripts(new ScriptWorkProgress(2, 2, string.Empty), "reempacotando");
            return Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.True(live);
    }

}
