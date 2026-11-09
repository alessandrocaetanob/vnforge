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
    public async Task Cada_configuracao_traduz_as_mesmas_linhas_aprovadas_e_o_chrf_compara_com_a_aprovada()
    {
        var providers = new Dictionary<string, FakeTranslationProvider> { ["A"] = EvalProject.Perfect(), ["B"] = new(unit => $"[PT] {unit.Source}") };

        var round = await EvalRunner.RunAsync(Request(c => new EvalProvider(providers[c.Id], () => null), Sonnet, Haiku), TestContext.Current.CancellationToken);

        Assert.Equal(20, round.GoldLines);
        Assert.Equal(20, round.RemainingLines);
        Assert.Equal(20, round.Sample.Count);
        Assert.Equal(_project.GoldIndexes.Select(EvalProject.Id), round.Reports[0].Lines.Select(l => l.LineId));
        Assert.Equal(round.Reports[0].Lines.Select(l => l.LineId), round.Reports[1].Lines.Select(l => l.LineId));
        Assert.Equal(100.0, round.Reports[0].MeanChrf, 6);
        Assert.InRange(round.Reports[1].MeanChrf, 0, 60);
        Assert.Equal(["A", "B"], round.Reports.Select(r => r.Config.Id));
    }

    [Fact]
    public async Task O_provedor_recebe_o_original_sem_a_traducao_aprovada_e_o_contexto_da_configuracao()
    {
        var one = EvalProject.Perfect();
        var two = EvalProject.Perfect();

        await EvalRunner.RunAsync(Request(c => new EvalProvider(c.Id == "A" ? one : two, () => null), Sonnet, Haiku), TestContext.Current.CancellationToken);

        var first = one.ReceivedBatches.OrderBy(b => b.Lines[0].Id, StringComparer.Ordinal).First();
        var second = two.ReceivedBatches.OrderBy(b => b.Lines[0].Id, StringComparer.Ordinal).First();
        Assert.All(first.Lines, line => Assert.Null(line.Translation));
        Assert.Equal(2, first.PreviousLines.Count);
        Assert.Equal(5, second.PreviousLines.Count);
        Assert.Equal(2, first.NextLines.Count);
        Assert.Equal("Contexto 4", first.PreviousLines[^1].Translation);
    }

    [Fact]
    public async Task Nao_grava_nada_nos_textos_do_projeto()
    {
        var before = _project.TextJson;

        await EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet), TestContext.Current.CancellationToken);

        Assert.Equal(before, _project.TextJson);
    }

    [Fact]
    public async Task Grava_a_rodada_e_as_configuracoes_em_cache_eval()
    {
        var round = await EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet, Haiku), TestContext.Current.CancellationToken);

        var loaded = EvalStore.LoadLatest(_project.Folder)!;
        Assert.Equal(round.Id, loaded.Id);
        Assert.Equal([Sonnet, Haiku], EvalStore.LoadConfigs(_project.Folder));
    }

    [Fact]
    public async Task Gera_os_pares_as_cegas_entre_traducoes_diferentes()
    {
        var providers = new Dictionary<string, FakeTranslationProvider> { ["A"] = EvalProject.Perfect(), ["B"] = new(unit => $"[PT] {unit.Source}") };

        var round = await EvalRunner.RunAsync(Request(c => new EvalProvider(providers[c.Id], () => null), Sonnet, Haiku), TestContext.Current.CancellationToken);

        Assert.Equal(EvalRunRequest.DefaultPairCount, round.Pairs.Count);
        Assert.All(round.Pairs, p => Assert.Equal(["A", "B"], new[] { p.First, p.Second }.Order()));
    }

    [Fact]
    public async Task O_juiz_recebe_original_e_texto_sem_saber_a_configuracao_e_a_nota_entra_no_relatorio()
    {
        var judge = new FakeJudgeProvider(unit => unit.Translation!.StartsWith("Tradução", StringComparison.Ordinal) ? 9 : 4);
        var providers = new Dictionary<string, FakeTranslationProvider> { ["A"] = EvalProject.Perfect(), ["B"] = new(unit => $"[PT] {unit.Source}") };
        var request = Request(c => new EvalProvider(providers[c.Id], () => null), Sonnet, Haiku) with { Judge = judge, JudgeModel = ClaudeModels.Haiku };

        var round = await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(9.0, round.Reports[0].MeanJudge);
        Assert.Equal(4.0, round.Reports[1].MeanJudge);
        Assert.Equal(ClaudeModels.Haiku, round.JudgeModel);
        Assert.Equal(2, judge.ReceivedBatches.Count);
        Assert.All(judge.ReceivedBatches.SelectMany(b => b.Lines), line =>
        {
            Assert.StartsWith("原文", line.Source, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(line.Translation));
        });
        Assert.All(judge.ReceivedBatches, b => Assert.Empty(b.PreviousLines));
    }

    [Fact]
    public async Task Sem_juiz_a_rodada_fica_sem_nota()
    {
        var round = await EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet), TestContext.Current.CancellationToken);

        Assert.Null(round.JudgeModel);
        Assert.Null(round.Reports[0].MeanJudge);
    }

    [Fact]
    public async Task O_custo_vem_dos_tokens_do_provedor_e_o_gasto_entra_no_registro_do_projeto()
    {
        var usage = new ClaudeUsage(1_000_000, 0, 0, 0);
        var judge = new FakeJudgeProvider { UsagePerCall = new ClaudeUsage(500_000, 0, 0, 0) };
        var request = Request(Same(EvalProject.Perfect(), usage), Sonnet) with { Judge = judge, JudgeModel = ClaudeModels.Haiku };

        var round = await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        var report = round.Reports[0];
        Assert.Equal(2.00m, report.Dollars);
        Assert.Equal(0.05m, report.JudgeDollars);
        Assert.Equal(1_000_000, report.InputTokens);
        Assert.Equal(40m, report.ProjectedDollars(400));
        Assert.Equal(2.05m, AiUsageLog.Load(_project.Folder).Dollars);
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
    public async Task Cancelar_nao_grava_rodada()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet), cts.Token));

        Assert.Null(EvalStore.LoadLatest(_project.Folder));
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
    }

    [Fact]
    public async Task Cada_lote_e_um_trecho_de_linhas_seguidas_e_a_linha_do_meio_tem_a_janela_de_contexto_real()
    {
        using var project = new EvalProject(goldCount: 100);
        var provider = EvalProject.Perfect();
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, Same(provider)) { RunOptions = Quick };

        await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        var batches = provider.ReceivedBatches.OrderBy(b => b.Lines[0].Id, StringComparer.Ordinal).ToList();
        Assert.Equal(Enumerable.Repeat(10, 10), batches.Select(b => b.Lines.Count));
        Assert.All(batches, b => Assert.Equal(
            Enumerable.Range(Index(b.Lines[0]), b.Lines.Count).Select(EvalProject.Id),
            b.Lines.Select(l => l.Id)));
        var second = batches[1];
        Assert.Contains(EvalProject.Id(19), second.Lines.Select(l => l.Id));
        Assert.Contains(EvalProject.Id(20), second.Lines.Select(l => l.Id));
        Assert.Contains(EvalProject.Id(21), second.Lines.Select(l => l.Id));
        Assert.Equal(["Tradução 13", "Tradução 14"], second.PreviousLines.Select(l => l.Translation));
        Assert.Equal(["原文25", "原文26"], second.NextLines.Take(2).Select(l => l.Source));
    }

    [Fact]
    public async Task Linhas_aprovadas_separadas_por_outras_nao_formam_um_trecho()
    {
        using var project = new EvalProject(spaced: true);
        var provider = EvalProject.Perfect();
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, Same(provider)) { RunOptions = Quick };

        await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(20, provider.ReceivedBatches.Count);
        Assert.All(provider.ReceivedBatches, b => Assert.Single(b.Lines));
    }

    [Fact]
    public async Task O_chrf_do_relatorio_e_o_do_conjunto()
    {
        var provider = new FakeTranslationProvider(unit => $"[PT] {unit.Source}");

        var round = await EvalRunner.RunAsync(Request(Same(provider), Sonnet), TestContext.Current.CancellationToken);

        var expected = Chrf.Corpus(_project.GoldIndexes.Select(i => ((string?)$"Tradução {i}", (string?)$"[PT] 原文{i}")));
        var report = round.Reports[0];
        Assert.Equal(expected, report.CorpusChrf!.Value, 9);
        Assert.Equal(expected, report.MeanChrf, 9);
    }

    [Fact]
    public async Task Para_no_meio_quando_o_gasto_passa_do_limite_e_nao_grava_a_rodada()
    {
        using var project = new EvalProject(goldCount: 100);
        var provider = new MeteredProvider();
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, _ => new EvalProvider(provider, provider.Usage))
        {
            RunOptions = Quick with { MaxConcurrency = 1 },
            Rate = new ExchangeRate(5m),
            Limits = new BudgetLimits(15m, null),
        };

        var error = await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("limite", error.Message);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(4m, AiUsageLog.Load(project.Folder).Dollars);
        Assert.Null(EvalStore.LoadLatest(project.Folder));
    }

    [Fact]
    public async Task Sem_limite_a_rodada_nao_para_por_gasto()
    {
        using var project = new EvalProject(goldCount: 100);
        var provider = new MeteredProvider();
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, _ => new EvalProvider(provider, provider.Usage)) { RunOptions = Quick };

        await EvalRunner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(10, provider.Calls);
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
    public async Task A_rodada_que_termina_inteira_nao_e_parcial()
    {
        var round = await EvalRunner.RunAsync(Request(Same(EvalProject.Perfect()), Sonnet), TestContext.Current.CancellationToken);

        Assert.False(round.IsPartial);
        Assert.Null(EvalStore.LoadLatest(_project.Folder)!.PartialNote);
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

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
