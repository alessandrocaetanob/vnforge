using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.AI.Tests.Eval;

public sealed class EvalRunnerTests : IDisposable
{
    private readonly EvalProject _project = new();

    public void Dispose() => _project.Dispose();

    private static readonly EvalConfig Sonnet = new("A", ClaudeModels.Sonnet, "medium", 2, false);
    private static readonly EvalConfig Haiku = new("B", ClaudeModels.Haiku, "low", 5, false);

    private static TranslationRunOptions Quick { get; } = new()
    {
        MaxRetries = 0,
        InitialBackoff = TimeSpan.Zero,
        Delay = static (_, _) => Task.CompletedTask,
    };

    private EvalRunRequest Request(Func<EvalConfig, EvalProvider> providers, params EvalConfig[] configs) =>
        new(_project.Folder, configs, 100, 3, providers) { RunOptions = Quick };

    private static Func<EvalConfig, EvalProvider> Same(ITranslationProvider provider, ClaudeUsage? usage = null) =>
        _ => new EvalProvider(provider, () => usage);

    [Fact]
    public async Task Nao_grava_nada_nos_textos_do_projeto()
    {
        var before = _project.TextJson;

        await EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet), TestContext.Current.CancellationToken);

        Assert.Equal(before, _project.TextJson);
    }

    [Fact]
    public async Task Configuracao_com_biblia_e_recusada_antes_de_gastar_quando_nao_ha_biblia()
    {
        var calls = 0;
        var request = Request(c =>
        {
            calls++;
            return new EvalProvider(EvalProject.Perfect(), () => null);
        }, Sonnet with { WithBible = true });

        var error = await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("A pede a bíblia do jogo", error.Message);
        Assert.Equal(0, calls);
        Assert.Null(EvalStore.LoadLatest(_project.Folder));
    }

    [Fact]
    public async Task Com_biblia_disponivel_so_as_configuracoes_que_a_pedem_a_recebem()
    {
        var withBible = EvalProject.Perfect();
        var without = EvalProject.Perfect();
        var request = Request(c => new EvalProvider(c.Id == "A" ? withBible : without, () => null), Sonnet with { WithBible = true }, Haiku)
            with { ApplyBible = batch => new TranslationBatch(batch.Lines) { StyleGuide = "Resumo da cena" } };

        await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.All(withBible.ReceivedBatches, b => Assert.Equal("Resumo da cena", b.StyleGuide));
        Assert.All(without.ReceivedBatches, b => Assert.Equal(string.Empty, b.StyleGuide));
    }

    [Fact]
    public async Task Projeto_sem_linhas_aprovadas_diz_o_que_fazer()
    {
        using var empty = new EvalProject(withGold: false);

        var error = await Assert.ThrowsAsync<EvalException>(() =>
            EvalRunner.RunAsync(new EvalRunRequest(empty.Folder, [Sonnet], 10, 1, Same(EvalProject.Perfect())), TestContext.Current.CancellationToken));

        Assert.Contains("ainda não tem linhas aprovadas", error.Message);
    }

    [Fact]
    public async Task Lote_que_falha_deixa_as_linhas_recusadas_com_chrf_zero_e_o_erro_no_relatorio()
    {
        var failing = new FakeTranslationProvider { FailOnCall = 1 };

        var round = await EvalRunner.RunAsync(Request(Same(failing), Sonnet), TestContext.Current.CancellationToken);

        var report = round.Reports[0];
        Assert.Equal(10, report.RefusedLines);
        Assert.All(report.Lines.Where(l => l.Refused), l => Assert.Equal(0.0, l.Chrf));
        Assert.Contains("Falha simulada", report.Error);
        Assert.Empty(round.Pairs);
    }

    [Fact]
    public async Task Informa_o_progresso_de_cada_configuracao_e_fase()
    {
        var seen = new List<EvalProgress>();
        var request = Request(Same(EvalProject.Perfect()), Sonnet, Haiku) with { Judge = new FakeJudgeProvider(), JudgeModel = ClaudeModels.Haiku, Progress = new SyncProgress<EvalProgress>(seen.Add) };

        await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(
            [new EvalProgress(1, 2, "A", EvalPhase.Translating), new EvalProgress(1, 2, "A", EvalPhase.Judging), new EvalProgress(2, 2, "B", EvalPhase.Translating), new EvalProgress(2, 2, "B", EvalPhase.Judging)],
            seen);
    }

    [Fact]
    public async Task Configuracao_invalida_e_recusada()
    {
        var bad = Sonnet with { Model = "gpt-5" };

        await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), bad), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet, Sonnet), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(Request(Same(EvalProject.Perfect())), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet with { Effort = "enorme" }), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet with { ContextLines = EvalConfigParser.MaxContextLines + 1 }), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Juiz_sem_modelo_para_o_preco_e_recusado()
    {
        var request = Request(Same(EvalProject.Perfect()), Sonnet) with { Judge = new FakeJudgeProvider() };

        var error = await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("precisa de um modelo", error.Message);
    }

    [Fact]
    public async Task Juiz_que_falha_deixa_o_erro_no_relatorio_e_as_linhas_sem_nota()
    {
        var request = Request(Same(EvalProject.Perfect()), Sonnet) with { Judge = new BrokenJudge(), JudgeModel = ClaudeModels.Haiku };

        var round = await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        var report = round.Reports[0];
        Assert.Contains("O juiz de IA falhou: sem rede", report.Error);
        Assert.All(report.Lines, l => Assert.Null(l.JudgeScore));
    }

    [Fact]
    public async Task Configuracao_que_nao_termina_no_limite_nao_derruba_as_que_terminaram_e_a_rodada_fica_parcial()
    {
        using var project = new EvalProject(goldCount: 100);
        var first = new MeteredProvider();
        var second = new MeteredProvider();
        var request = new EvalRunRequest(project.Folder, [Sonnet, Sonnet with { Id = "B", ContextLines = 5 }], 100, 3, c => c.Id == "A" ? new EvalProvider(first, first.Usage) : new EvalProvider(second, second.Usage))
        {
            RunOptions = Quick with { MaxConcurrency = 1 },
            Rate = new ExchangeRate(5m),
            Limits = new BudgetLimits(105m, null),
        };

        var round = await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(["A"], round.Reports.Select(r => r.Config.Id));
        Assert.True(round.IsPartial);
        Assert.Contains("1 de 2", round.PartialNote);
        Assert.Equal(round.Id, EvalStore.LoadLatest(project.Folder)!.Id);
        Assert.Equal(1, second.Calls);
        Assert.Equal((10 + 1) * 2m, AiUsageLog.Load(project.Folder).Dollars);
    }

    [Fact]
    public async Task O_juiz_tambem_para_antes_do_lote_que_passaria_do_limite()
    {
        using var project = new EvalProject(goldCount: 100);
        var judge = new FakeJudgeProvider { UsagePerCall = new ClaudeUsage(500_000, 0, 0, 0) };
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, Same(EvalProject.Perfect()))
        {
            RunOptions = Quick,
            Judge = judge,
            JudgeModel = ClaudeModels.Haiku,
            Rate = new ExchangeRate(5m),
            Limits = new BudgetLimits(0.30m, null),
        };

        var error = await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("limite", error.Message);
        Assert.Equal(2, judge.ReceivedBatches.Count);
        Assert.Equal(0.10m, AiUsageLog.Load(project.Folder).Dollars);
    }

    [Fact]
    public async Task Cancelar_no_juiz_nao_perde_o_que_ele_ja_gastou()
    {
        using var project = new EvalProject(goldCount: 100);
        using var cts = new CancellationTokenSource();
        var judge = new FakeJudgeProvider(_ =>
        {
            cts.Cancel();
            return 7;
        }) { UsagePerCall = new ClaudeUsage(500_000, 0, 0, 0) };
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, Same(EvalProject.Perfect())) { RunOptions = Quick, Judge = judge, JudgeModel = ClaudeModels.Haiku };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvalRunner.RunAsync(request, cts.Token));

        Assert.Equal(0.05m, AiUsageLog.Load(project.Folder).Dollars);
        Assert.Null(EvalStore.LoadLatest(project.Folder));
    }

    private static int Index(VNForge.Core.Text.TextUnit unit) => int.Parse(unit.Id[(unit.Id.IndexOf(':', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Um provedor que gasta 1 milhão de tokens de entrada por chamada, para a rodada ver o gasto crescer.</summary>
    private sealed class MeteredProvider : ITranslationProvider
    {
        private readonly FakeTranslationProvider _inner = EvalProject.Perfect();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ClaudeUsage? Usage() => new(Calls * 1_000_000L, 0, 0, 0);

        public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            var results = await _inner.TranslateAsync(batch, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _calls);
            return results;
        }
    }

    private sealed class BrokenJudge : IJudgeProvider
    {
        public ClaudeUsage? Usage => null;

        public Task<IReadOnlyList<JudgeScore>> JudgeAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<JudgeScore>>(new InvalidOperationException("sem rede"));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
