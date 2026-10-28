using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class TranslationJobTests
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));

    private static TranslationStatus[] Untranslated(int count) => [.. Enumerable.Repeat(TranslationStatus.Untranslated, count)];

    private static TranslationRequest Request(TranslationQuality quality = TranslationQuality.Careful) =>
        new(TranslationScope.Scene, quality, ["yst00001"]);

    private static TranslationJob Job(
        ProjectFolder folder,
        UsageProvider provider,
        BudgetLimits? limits = null,
        Func<BudgetQuestion, CancellationToken, Task<bool>>? confirm = null) =>
        new(
            folder,
            (_, _) => provider,
            new TranslationJobSettings(Rate, limits ?? BudgetLimits.None, 1),
            confirm,
            p => (p as UsageProvider)?.Usage,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Grava_a_traducao_das_pendentes_como_da_IA_e_deixa_o_resto()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Approved, TranslationStatus.Untranslated));
        var provider = new UsageProvider();

        var result = await Job(folder, provider).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        var saved = folder.LoadText("yst00001");
        Assert.Equal(2, result.Translated);
        Assert.Null(result.Stopped);
        Assert.Null(result.Problem);
        Assert.Equal(TranslationStatus.Machine, saved[0].Status);
        Assert.Equal("[PT] 原文0", saved[0].Translation);
        Assert.Equal("claude-opus-5-5", saved[0].Origin?.Model);
        Assert.Equal(TranslationStatus.Approved, saved[1].Status);
        Assert.Equal("tradução 1", saved[1].Translation);
    }

    [Fact]
    public async Task Grava_a_duvida_da_IA_na_linha_e_a_retraducao_troca_a_duvida_antiga()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Untranslated));
        var doubt = new TranslationDoubt(DoubtKind.Pun, "Jogo de palavras com 'hashi'.");
        var provider = new UsageProvider { DoubtFor = u => u.Source.EndsWith('0') ? doubt : null };

        await Job(folder, provider).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        var saved = folder.LoadText("yst00001");
        Assert.Equal(doubt, saved[0].Doubt);
        Assert.Null(saved[1].Doubt);

        folder.SaveText("yst00001", [saved[0] with { Status = TranslationStatus.Untranslated }, saved[1] with { Doubt = doubt, Status = TranslationStatus.Untranslated }]);
        await Job(folder, new UsageProvider()).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        Assert.All(folder.LoadText("yst00001"), u => Assert.Null(u.Doubt));
    }

    [Fact]
    public async Task So_traduz_os_scripts_do_escopo()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", TranslationStatus.Untranslated));
        var provider = new UsageProvider();

        var result = await Job(folder, provider).RunAsync(new TranslationRequest(TranslationScope.Scene, TranslationQuality.Fast, ["outro"]), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Translated);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(TranslationStatus.Untranslated, folder.LoadText("yst00001")[0].Status);
    }

    [Fact]
    public async Task A_qualidade_escolhida_define_o_modelo_do_pedido()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", TranslationStatus.Untranslated));
        string? asked = null;
        var job = new TranslationJob(
            folder,
            (model, _) =>
            {
                asked = model;
                return new UsageProvider();
            },
            new TranslationJobSettings(Rate, BudgetLimits.None, 1));

        await job.RunAsync(Request(TranslationQuality.Fast), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(ClaudeModels.Haiku, asked);
    }

    [Fact]
    public async Task Para_quando_o_gasto_chega_ao_limite_e_deixa_o_resto_pendente()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(90)));
        var provider = new UsageProvider { OutputTokensPerCall = 1_000_000 };

        var result = await Job(folder, provider, new BudgetLimits(50m, null)).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(BudgetStop.LimitReached, result.Stopped);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(30, result.Translated);
        Assert.Equal(100m, result.SpentBrl);
        Assert.Equal(60, folder.LoadText("yst00001").Count(u => u.Status == TranslationStatus.Untranslated));
    }

    [Fact]
    public async Task O_gasto_ja_registrado_no_projeto_conta_para_o_limite()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(30)));
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 10m, 1));
        var provider = new UsageProvider();

        var result = await Job(folder, provider, new BudgetLimits(50m, null)).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(BudgetStop.LimitReached, result.Stopped);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, result.Translated);
    }

    [Fact]
    public async Task Pedido_acima_do_aviso_sem_confirmacao_nao_gasta_nada()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(30)));
        var provider = new UsageProvider();

        var result = await Job(folder, provider, new BudgetLimits(null, 0.0001m)).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(BudgetStop.Declined, result.Stopped);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Pedido_acima_do_aviso_segue_quando_a_pessoa_confirmou()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(30)));
        var provider = new UsageProvider();
        BudgetQuestion? asked = null;

        var result = await Job(
            folder,
            provider,
            new BudgetLimits(null, 0.0001m),
            (question, _) =>
            {
                asked = question;
                return Task.FromResult(true);
            }).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Null(result.Stopped);
        Assert.Equal(30, result.Translated);
        Assert.NotNull(asked);
    }

    [Fact]
    public async Task Soma_o_gasto_da_execucao_ao_registro_do_projeto_e_ao_relato_da_tarefa()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(30)));
        var provider = new UsageProvider { OutputTokensPerCall = 100_000 };
        var reporter = new Reporter();

        await Job(folder, provider).RunAsync(Request(), reporter, TestContext.Current.CancellationToken);

        Assert.Equal(2m, AiUsageLog.Load(folder).Dollars);
        Assert.Equal(2m, reporter.Cost);
        Assert.Equal(1d, reporter.LastFraction);
    }

    [Fact]
    public async Task Lote_que_falha_vira_aviso_em_portugues_e_as_linhas_seguem_pendentes()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(30)));
        var job = new TranslationJob(
            folder,
            (_, _) => new FailingProvider(),
            new TranslationJobSettings(Rate, BudgetLimits.None, 1, (_, _) => Task.CompletedTask));

        var result = await job.RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Translated);
        Assert.Equal(30, result.Failed);
        Assert.Equal("Erro inesperado ao falar com a IA.", result.Problem);
        Assert.All(folder.LoadText("yst00001"), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
    }

    [Fact]
    public async Task Edicao_feita_no_Editor_no_meio_do_lote_sobrevive_e_a_IA_nao_sobrescreve_a_linha_traduzida_a_mao()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(30)));
        var editor = new LineEditor(folder, TimeProvider.System);
        var provider = new UsageProvider
        {
            DuringCall = () =>
            {
                editor.SetTranslation("yst00001", "yst00001:000005", "à mão");
                editor.SetNotes("yst00001", "yst00001:000006", "nota da pessoa");
            },
        };

        var result = await Job(folder, provider).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        var saved = folder.LoadText("yst00001");
        Assert.Equal(29, result.Translated);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("à mão", saved[5].Translation);
        Assert.Equal(TranslationStatus.Translated, saved[5].Status);
        Assert.Equal(TranslationStatus.Machine, saved[6].Status);
        Assert.Equal("nota da pessoa", saved[6].Notes);
        Assert.Equal(29, saved.Count(u => u.Status == TranslationStatus.Machine));
    }

    [Fact]
    public async Task Linha_aprovada_no_meio_da_execucao_nao_e_desfeita_pela_gravacao_do_lote()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(60)));
        var editor = new LineEditor(folder, TimeProvider.System);
        var calls = 0;
        var provider = new UsageProvider
        {
            DuringCall = () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    editor.SetTranslation("yst00001", "yst00001:000040", "revisada");
                    editor.Approve("yst00001", "yst00001:000040");
                }
            },
        };

        var result = await Job(folder, provider).RunAsync(Request(), new Reporter(), TestContext.Current.CancellationToken);

        var saved = folder.LoadText("yst00001");
        Assert.Equal(1, result.Skipped);
        Assert.Equal(TranslationStatus.Approved, saved[40].Status);
        Assert.Equal("revisada", saved[40].Translation);
        Assert.Equal(59, result.Translated);
    }

    [Fact]
    public async Task Cancelar_interrompe_registra_o_gasto_e_deixa_o_que_voltou()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProjects.Create(temp.Path, "Teste", Untranslated(90)));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new UsageProvider { OutputTokensPerCall = 100_000, DuringCall = cts.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job(folder, provider).RunAsync(Request(), new Reporter(), cts.Token));

        Assert.True(AiUsageLog.Load(folder).Dollars > 0m);
        Assert.Equal(1, AiUsageLog.Load(folder).Runs);
        Assert.NotEqual(0, folder.LoadText("yst00001").Count(u => u.Status == TranslationStatus.Untranslated));
    }

    private sealed class Reporter : IBackgroundTaskReporter
    {
        public decimal Cost { get; private set; }

        public double? LastFraction { get; private set; }

        public void Report(double? fraction, string? message = null) => LastFraction = fraction;

        public void AddCost(decimal usd) => Cost += usd;
    }

    private sealed class FailingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new InvalidOperationException("Falha simulada.");
    }

    private sealed class UsageProvider : ITranslationProvider
    {
        private long output;
        private int calls;

        public long OutputTokensPerCall { get; init; }

        public Action? DuringCall { get; init; }

        public Func<TextUnit, TranslationDoubt?>? DoubtFor { get; init; }

        public int Calls => Volatile.Read(ref calls);

        public ClaudeUsage Usage => new(0, Interlocked.Read(ref output), 0, 0);

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            Interlocked.Add(ref output, OutputTokensPerCall);
            DuringCall?.Invoke();
            IReadOnlyList<TranslationResult> results = [.. batch.Lines.Select(u => new TranslationResult(u.Id, $"[PT] {u.Source}", Doubt: DoubtFor?.Invoke(u)))];
            return Task.FromResult(results);
        }
    }
}
