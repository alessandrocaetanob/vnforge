using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class EvalCommandTests : IDisposable
{
    private const string Key = "yst00001";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly List<string> _models = [];
    private readonly FakeJudgeProvider _judge = new(unit => unit.Translation!.StartsWith("Tradução", StringComparison.Ordinal) ? 9 : 4);

    public EvalCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText(Key, [.. Enumerable.Range(1, 60).Select(Unit)]);
        _cli.Cli.TranslationProviderFactory = (model, _) =>
        {
            _models.Add(model);
            return model == ClaudeModels.Haiku ? new FakeTranslationProvider(u => $"[PT] {u.Source}") : new FakeTranslationProvider(u => $"Tradução {u.Source[2..]}");
        };
        _cli.Cli.JudgeProviderFactory = (_, _) => _judge;
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int i) => i % 3 == 0
        ? new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"原文{i}", null, TranslationStatus.Untranslated)
        : new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"原文{i}", $"Tradução {i}", TranslationStatus.Approved);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["eval", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private string TextJson => File.ReadAllText(_project.TextPath(Key));

    [Fact]
    public async Task Roda_as_configuracoes_e_escreve_o_relatorio_em_portugues()
    {
        var result = await Run("--config", "sonnet:medium:5", "--config", "haiku:low:5", "--sample", "40");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("40 linhas sorteadas de 40 aprovadas", result.Output);
        Assert.Contains("A  Sonnet 5.5 · effort médio · 5 falas · sem bíblia", result.Output);
        Assert.Contains("B  Haiku 5.5 · effort baixo · 5 falas · sem bíblia", result.Output);
        Assert.Contains("chrF 100,0", result.Output);
        Assert.Contains("juiz 9,0", result.Output);
        Assert.Contains("às cegas sem rodada", result.Output);
        Assert.Contains("chrF: quanto a tradução se parece com a aprovada", result.Output);
        Assert.Contains("20 linhas que faltam", result.Output);
        Assert.Contains("Sugestão para o M4: A.", result.Output);
        Assert.Equal([ClaudeModels.Sonnet, ClaudeModels.Haiku], _models);
    }

    private void WriteAppJson(string json)
    {
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), json);
    }

    [Fact]
    public async Task Recusa_a_rodada_cuja_estimativa_passa_do_que_resta_do_limite_e_nao_gasta_nada()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 10 }""");
        AiUsageLog.TryRecord(_project, new AiUsageTotals(0, 0, 0, 0, 1.99m, 1));

        var result = await Run("--config", "opus:high:30", "--config", "opus:high:30", "--config", "opus:high:30", "--sample", "40", "--no-judge");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("só restam", result.Error + result.Output);
        Assert.Contains("limite de gasto", result.Error + result.Output);
        Assert.Empty(_models);
        Assert.Null(EvalStore.LoadLatest(_project));
    }

    [Fact]
    public async Task Nao_grava_nada_nos_textos_do_projeto()
    {
        var before = TextJson;

        await Run("--config", "sonnet", "--sample", "10");

        Assert.Equal(before, TextJson);
        Assert.NotNull(EvalStore.LoadLatest(_project));
    }

    [Fact]
    public async Task Sem_juiz_a_nota_fica_de_fora()
    {
        var result = await Run("--config", "sonnet", "--sample", "10", "--no-judge");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("juiz sem juiz", result.Output);
        Assert.Empty(_judge.ReceivedBatches);
    }

    [Fact]
    public async Task Dry_run_estima_sem_chamar_o_provedor_nem_gravar_a_rodada()
    {
        var result = await Run("--config", "opus:high", "--config", "haiku", "--sample", "40", "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Estimativa do banco de provas: 40 linhas sorteadas de 40 aprovadas, em 2 configurações.", result.Output);
        Assert.Contains("Custo aproximado da rodada: R$", result.Output);
        Assert.Empty(_models);
        Assert.Empty(_judge.ReceivedBatches);
        Assert.Null(EvalStore.LoadLatest(_project));
    }

    [Fact]
    public async Task Report_mostra_a_ultima_rodada_sem_rodar_e_sem_rodada_diz_o_que_fazer()
    {
        var empty = await Run("--report");
        Assert.Equal(1, empty.ExitCode);
        Assert.Contains("Ainda não há rodada neste projeto.", empty.Error);

        await Run("--config", "sonnet", "--sample", "10");
        _models.Clear();
        var report = await Run("--report");

        Assert.Equal(0, report.ExitCode);
        Assert.Contains("A  Sonnet 5.5", report.Output);
        Assert.Empty(_models);
    }

    [Fact]
    public async Task Use_marca_a_configuracao_escolhida_para_o_m4()
    {
        await Run("--config", "sonnet", "--config", "haiku", "--sample", "10");

        var chosen = await Run("--use", "b");
        var report = await Run("--report");

        Assert.Equal(0, chosen.ExitCode);
        Assert.Contains("Configuração B (Haiku 5.5 · effort médio · sem bíblia) escolhida para o M4.", chosen.Output);
        Assert.Equal("B", EvalStore.LoadLatest(_project)!.ChosenConfigId);
        Assert.Contains("O vnforge.json passa a usar claude-haiku-5-5, effort medium e 15 falas de contexto de cada lado.", chosen.Output);
        var ai = _project.LoadConfig().Ai;
        Assert.Equal((ClaudeModels.Haiku, "medium", 15, 15), (ai.Model, ai.Effort, ai.ContextBefore, ai.ContextAfter));
        Assert.Contains("B  Haiku 5.5 · effort médio · 15 falas · sem bíblia · escolhida para o M4", report.Output);
        Assert.DoesNotContain("Sugestão para o M4", report.Output);
    }

    [Fact]
    public async Task Use_com_letra_que_a_rodada_nao_tem_e_recusado()
    {
        await Run("--config", "sonnet", "--sample", "10");

        var result = await Run("--use", "Z");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("não tem a configuração 'Z'", result.Error);
    }

    [Fact]
    public async Task Configuracao_com_biblia_e_recusada_enquanto_o_projeto_nao_tem_biblia()
    {
        var result = await Run("--config", "sonnet:medium:15:com-biblia");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("pede a bíblia do jogo", result.Error);
        Assert.Empty(_models);
    }

    [Theory]
    [InlineData("--sample", "0")]
    [InlineData("--sample", "99999")]
    [InlineData("--pairs", "-1")]
    [InlineData("--config", "gpt")]
    [InlineData("--judge-model", "gpt")]
    public async Task Opcao_invalida_sai_com_erro_de_uso(string option, string value)
    {
        var result = await Run(option, value);

        Assert.Equal(1, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(_models);
    }

    [Fact]
    public async Task O_conjunto_da_rodada_anterior_vale_quando_nenhuma_configuracao_e_pedida()
    {
        await Run("--config", "haiku:low:3", "--sample", "10");
        _models.Clear();

        var result = await Run("--sample", "10", "--json");

        using var doc = JsonDocument.Parse(result.Output);
        var configs = doc.RootElement.GetProperty("data").GetProperty("configs");
        Assert.Equal(1, configs.GetArrayLength());
        Assert.Equal(3, configs[0].GetProperty("contextLines").GetInt32());
        Assert.Equal([ClaudeModels.Haiku], _models);
    }
}
