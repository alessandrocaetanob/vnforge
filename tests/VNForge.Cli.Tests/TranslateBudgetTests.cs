using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

/// <summary>O <c>vnf translate</c> respeita o limite de gasto e o valor de aviso que o app guarda em <c>app.json</c>.</summary>
public sealed class TranslateBudgetTests : IDisposable
{
    private const string UnitId = "yst00001:000001";

    private readonly CliHarness _cli;
    private readonly ProjectFolder _project;
    private readonly string _projectPath;
    private readonly CountingClient _client = new();

    public TranslateBudgetTests()
    {
        _cli = new CliHarness();
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText("yst00001", [new TextUnit(UnitId, TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        _cli.Cli.TranslationProviderFactory = (_, _) => new ClaudeTranslationProvider(_client);
        Directory.CreateDirectory(_cli.UserConfigDirectory);
    }

    public void Dispose() => _cli.Dispose();

    private void WriteAppJson(string json) => File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), json);

    private TextUnit Saved() => _project.LoadText("yst00001")[0];

    [Fact]
    public async Task Sem_app_json_nao_ha_limite()
    {
        var result = await _cli.RunAsync(["translate", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(TranslationStatus.Machine, Saved().Status);
    }

    [Fact]
    public async Task Para_antes_de_gastar_quando_o_projeto_ja_chegou_ao_limite()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 50 }""");
        AiUsageLog.TryRecord(_project, new AiUsageTotals(0, 0, 0, 0, 10m, 1));

        var result = await _cli.RunAsync(["translate", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("limite de gasto", result.Output);
        Assert.Equal(0, _client.Calls);
        Assert.Equal(TranslationStatus.Untranslated, Saved().Status);
    }

    [Fact]
    public async Task Pedido_acima_do_aviso_pergunta_e_a_resposta_nao_nao_gasta_nada()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "warnAbove": 0.0001 }""");
        _cli.SetInput("n\n");

        var result = await _cli.RunAsync(["translate", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("não foi confirmado", result.Output);
        Assert.Contains("[s/N]", result.Error);
        Assert.Equal(0, _client.Calls);
        Assert.Equal(TranslationStatus.Untranslated, Saved().Status);
    }

    [Fact]
    public async Task Pedido_acima_do_aviso_segue_quando_a_pessoa_confirma()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "warnAbove": 0.0001 }""");
        _cli.SetInput("s\n");

        var result = await _cli.RunAsync(["translate", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, _client.Calls);
        Assert.Equal(TranslationStatus.Machine, Saved().Status);
    }

    [Fact]
    public async Task Yes_confirma_sem_perguntar()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "warnAbove": 0.0001 }""");

        var result = await _cli.RunAsync(["translate", "--project", _projectPath, "--yes"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("[s/N]", result.Error);
        Assert.Equal(TranslationStatus.Machine, Saved().Status);
    }

    [Fact]
    public async Task O_gasto_da_execucao_conta_e_o_json_diz_por_que_parou()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 50 }""");
        AiUsageLog.TryRecord(_project, new AiUsageTotals(0, 0, 0, 0, 10m, 1));

        var result = await _cli.RunAsync(["translate", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("limitReached", result.Output);
    }

    [Fact]
    public async Task Dry_run_ignora_o_orcamento()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 1 }""");
        AiUsageLog.TryRecord(_project, new AiUsageTotals(0, 0, 0, 0, 10m, 1));

        var result = await _cli.RunAsync(["translate", "--project", _projectPath, "--dry-run"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, _client.Calls);
    }

    private sealed class CountingClient : IClaudeMessageClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ClaudeReply("end_turn", $$"""{"translations":[{"id":"{{UnitId}}","translation":"Texto"}]}""", new ClaudeUsage(100, 50, 0, 0)));
        }
    }
}
