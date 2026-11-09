using System.Text.Json;
using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class TranslateBatchCommandTests : IDisposable
{
    private const string Key = "yst00001";
    private const string BatchFlag = "--batch";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly FakeMessageBatchClient _client = new();

    public TranslateBatchCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText(Key,
        [
            Unit(1, TranslationStatus.Untranslated),
            Unit(2, TranslationStatus.Approved),
            Unit(3, TranslationStatus.Untranslated),
            Unit(4, TranslationStatus.Untranslated),
        ]);
        _cli.Cli.MessageBatchClientFactory = () => _client;
        _cli.Cli.TranslationProviderFactory = (_, _) => throw new InvalidOperationException("O lote não usa a chamada de agora.");
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int n, TranslationStatus status) =>
        new($"{Key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : "pronta", status);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["translate", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private NightBatchState OnlyBatch() => Assert.Single(new NightBatchStore(_project).List());

    [Fact]
    public async Task Batch_envia_o_escopo_pela_API_de_lotes_e_nao_grava_nada_ainda()
    {
        var result = await Run(BatchFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Lote enviado: msgbatch_fake0001", result.Output, StringComparison.Ordinal);
        Assert.Contains("3 linhas", result.Output, StringComparison.Ordinal);
        Assert.Contains("metade do preço", result.Output, StringComparison.Ordinal);
        Assert.Contains("vnf translate --batch", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, _client.Creates);
        Assert.Equal(3, OnlyBatch().LineCount);
        Assert.All(_project.LoadText(Key).Where(u => u.Status != TranslationStatus.Approved), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
    }

    [Fact]
    public async Task Batch_de_novo_com_o_lote_em_andamento_mostra_o_andamento_e_nao_envia_outro()
    {
        await Run(BatchFlag);

        var again = await Run(BatchFlag);

        Assert.Equal(0, again.ExitCode);
        Assert.Contains("em andamento", again.Output, StringComparison.Ordinal);
        Assert.Contains("msgbatch_fake0001", again.Output, StringComparison.Ordinal);
        Assert.Equal(1, _client.Creates);
        Assert.Equal(1, _client.Gets);
    }

    [Fact]
    public async Task Batch_de_novo_depois_que_o_lote_terminou_grava_a_traducao_e_guarda_o_ponto_de_restauracao()
    {
        await Run(BatchFlag);
        _client.Finish("msgbatch_fake0001");

        var again = await Run(BatchFlag);

        Assert.Equal(0, again.ExitCode);
        Assert.Contains("chegou e foi gravado: 3 linhas traduzidas", again.Output, StringComparison.Ordinal);
        Assert.Contains("Desfazer", again.Output, StringComparison.Ordinal);
        var units = _project.LoadText(Key);
        Assert.Equal("[PT] 原文1", units[0].Translation);
        Assert.Equal(TranslationStatus.Machine, units[0].Status);
        Assert.Equal("anthropic", units[0].Origin!.Provider);
        Assert.Equal("pronta", units[1].Translation);
        var applied = OnlyBatch();
        Assert.Equal(NightBatchPhase.Applied, applied.Phase);
        Assert.Single(new RestorePointStore(_project).List());
        Assert.Equal(applied.Outcome!.RestorePointId, new RestorePointStore(_project).List()[0].Id);
    }

    [Fact]
    public async Task Linha_que_voltou_com_erro_fica_sem_traducao_e_o_comando_sai_com_erro()
    {
        await Run(BatchFlag);
        _client.OutcomeOf = _ => MessageBatchOutcome.Errored;
        _client.Finish("msgbatch_fake0001");

        var again = await Run(BatchFlag);

        Assert.Equal(1, again.ExitCode);
        Assert.Contains("3 linhas voltaram com erro", again.Output, StringComparison.Ordinal);
        Assert.Equal(TranslationStatus.Untranslated, _project.LoadText(Key)[0].Status);
    }

    [Fact]
    public async Task Linha_mudada_a_mao_durante_a_espera_nao_e_sobrescrita()
    {
        await Run(BatchFlag);
        new LineEditor(_project, TimeProvider.System).SetTranslation(Key, $"{Key}:000003", "à mão");
        _client.Finish("msgbatch_fake0001");

        var again = await Run(BatchFlag);

        Assert.Contains("2 linhas traduzidas", again.Output, StringComparison.Ordinal);
        Assert.Contains("1 linha foi mudada durante a espera", again.Output, StringComparison.Ordinal);
        Assert.Equal("à mão", _project.LoadText(Key)[2].Translation);
    }

    [Fact]
    public async Task Dry_run_com_batch_estima_a_metade_e_nao_chama_a_API()
    {
        var normal = await Run("--dry-run");
        var batch = await Run(BatchFlag, "--dry-run");

        Assert.Equal(0, batch.ExitCode);
        Assert.Contains("Estimativa", batch.Output, StringComparison.Ordinal);
        Assert.Contains("metade", batch.Output, StringComparison.Ordinal);
        Assert.Equal(0, _client.Creates);
        Assert.Equal(0, _client.Gets);
        Assert.NotEqual(normal.Output, batch.Output);
    }

    [Fact]
    public async Task Cancel_batch_pede_o_cancelamento_do_lote_em_aberto()
    {
        await Run(BatchFlag);

        var result = await Run("--cancel-batch");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Cancelamento pedido para o lote msgbatch_fake0001", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, _client.Cancels);
        Assert.Equal(NightBatchPhase.Canceling, OnlyBatch().Phase);
    }

    [Fact]
    public async Task Cancel_batch_sem_lote_em_aberto_diz_isso_sem_pedir_a_chave()
    {
        _cli.Cli.MessageBatchClientFactory = () => throw new InvalidOperationException("Não devia criar o cliente.");

        var result = await Run("--cancel-batch");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nenhum lote em aberto", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_traz_o_id_do_lote_enviado()
    {
        var result = await Run(BatchFlag, "--json");

        using var json = JsonDocument.Parse(result.Output);
        var batch = json.RootElement.GetProperty("data").GetProperty("batch");
        Assert.Equal("msgbatch_fake0001", batch.GetProperty("id").GetString());
        Assert.Equal("processing", batch.GetProperty("status").GetString());
        Assert.Equal(3, batch.GetProperty("lines").GetInt32());
    }

    [Fact]
    public async Task Sem_linha_pendente_nao_envia_lote()
    {
        _project.SaveText(Key, [Unit(1, TranslationStatus.Approved)]);

        var result = await Run(BatchFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nada a traduzir", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, _client.Creates);
    }
}
