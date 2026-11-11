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
