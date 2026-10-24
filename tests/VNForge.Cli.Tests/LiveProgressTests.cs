using VNForge.AI.Claude;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class LiveProgressTests
{
    private const string Descricao = "Trabalhando";

    [Fact]
    public void Sem_nenhuma_linha_pronta_o_tempo_restante_e_desconhecido()
    {
        Assert.Null(BatchProgressText.Remaining(TimeSpan.FromSeconds(5), done: 0, total: 100));
    }

    [Fact]
    public void Tempo_restante_segue_o_ritmo_ate_agora()
    {
        var left = BatchProgressText.Remaining(TimeSpan.FromSeconds(30), done: 25, total: 100);

        Assert.Equal(TimeSpan.FromSeconds(90), left);
    }

    [Fact]
    public void Tudo_pronto_nao_falta_tempo()
    {
        Assert.Equal(TimeSpan.Zero, BatchProgressText.Remaining(TimeSpan.FromSeconds(30), done: 100, total: 100));
    }

    [Theory]
    [InlineData(0.2, "1 s")]
    [InlineData(45, "45 s")]
    [InlineData(61, "2 min")]
    [InlineData(90, "2 min")]
    [InlineData(3000, "50 min")]
    [InlineData(3900, "1 h 05 min")]
    public void Tempo_restante_arredonda_para_cima(double seconds, string expected)
    {
        Assert.Equal(expected, BatchProgressText.FormatRemaining(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(360, 12)]
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
    public void Linha_de_andamento_sem_uso_nem_ritmo_omite_tokens_e_custo()
    {
        var text = BatchProgressText.Detail(0, 12, 0, 360, usage: null, dollars: null, TimeSpan.Zero);

        Assert.Equal("lote 0/12  0/360 linhas  calculando o tempo", text);
    }

    [Fact]
    public void Linha_de_andamento_no_fim_nao_promete_tempo()
    {
        var text = BatchProgressText.Detail(12, 12, 360, 360, usage: null, dollars: 1.5m, TimeSpan.FromSeconds(100));

        Assert.Equal("lote 12/12  360/360 linhas  US$ 1,50", text);
    }

    [Fact]
    public async Task Sem_terminal_interativo_o_trabalho_roda_sem_barra_e_o_destino_ignora_os_relatos()
    {
        var harness = ConsoleHarness.Plain();
        var live = true;

        var result = await LiveProgress.RunAsync(harness.Console, live: false, Descricao, total: 10, sink =>
        {
            live = sink.IsLive;
            sink.Report(5, "metade");
            sink.Scripts(new ScriptWorkProgress(1, 2, "yst00001.ybn"), "fim");
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.False(live);
        Assert.Equal(string.Empty, harness.Output);
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

    [Fact]
    public async Task Erro_no_trabalho_sobe_para_quem_chamou_com_a_barra_na_tela()
    {
        var harness = ConsoleHarness.For(TestTerminals.InteractivePlain());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LiveProgress.RunAsync<int>(harness.Console, live: true, Descricao, total: 3, _ => throw new InvalidOperationException("falhou")));

        Assert.Equal("falhou", error.Message);
    }
}
