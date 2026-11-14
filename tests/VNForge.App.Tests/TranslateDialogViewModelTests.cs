using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
[Trait("Risco", "R6")]
public sealed class TranslateDialogViewModelTests : IDisposable
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private static TextUnit Line(string key, int i, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"{key}:{i:D6}", TextUnitKind.Dialogue, "ハル", "今日はとてもいい天気ですね、散歩に行きませんか？", status == TranslationStatus.Untranslated ? null : "ok", status);

    /// <summary>Quatro scripts: yst00001 com 100 pendentes, yst00002 com 50, yst00003 sem pendentes e yst00004 com 20; todos na mesma faixa de capítulo.</summary>
    private ProjectFolder Project(string sub = "projeto")
    {
        var root = SampleProjects.Create(temp.SubFolder(sub), "Teste");
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [.. Enumerable.Range(0, 100).Select(i => Line("yst00001", i))]);
        folder.SaveText("yst00002", [.. Enumerable.Range(0, 50).Select(i => Line("yst00002", i))]);
        folder.SaveText("yst00003", [.. Enumerable.Range(0, 5).Select(i => Line("yst00003", i, TranslationStatus.Approved))]);
        folder.SaveText("yst00004", [.. Enumerable.Range(0, 20).Select(i => Line("yst00004", i))]);
        return folder;
    }

    private static TranslateDialogSettings Settings(BudgetLimits? limits = null, TranslationQuality quality = TranslationQuality.Careful, bool key = true) =>
        new(Rate, limits ?? BudgetLimits.None, quality, key);

    private static async Task<TranslateDialogViewModel> OpenAsync(ProjectFolder folder, TranslateDialogSettings settings, string? scene = "yst00001", IReadOnlyList<string>? chosen = null)
    {
        var vm = new TranslateDialogViewModel();
        await vm.OpenAsync(folder, scene, settings, chosen);
        return vm;
    }

    [Fact]
    public async Task Pedido_acima_do_limite_desliga_o_botao_e_oferece_a_qualidade_que_cabe()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(folder.LoadAllText(), ["yst00001"]), Rate);
        var limit = 10m + quotes.For(TranslationQuality.Balanced).Brl + 0.01m;

        var vm = await OpenAsync(folder, Settings(new BudgetLimits(limit, null)));
        vm.EditorPass = false;

        Assert.Equal(TranslationQuality.Careful, vm.SelectedQuality.Quality);
        Assert.True(vm.IsOverLimit);
        Assert.False(vm.CanStart);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.CanSwitchToFit);
        Assert.Contains("Equilibrada", vm.SwitchToFitText);
        Assert.Contains("passa do limite", vm.OverLimitText);

        vm.SwitchToFitCommand.Execute(null);

        Assert.Equal(TranslationQuality.Balanced, vm.SelectedQuality.Quality);
        Assert.False(vm.IsOverLimit);
        Assert.True(vm.CanStart);
        Assert.False(vm.CanSwitchToFit);
    }

    [Fact]
    public async Task O_limite_de_gasto_vale_sobre_a_metade_do_preco_no_lote()
    {
        var folder = Project();
        var normal = await OpenAsync(folder, Settings());
        var careful = normal.Qualities.Single(q => q.Quality == TranslationQuality.Careful).Price;

        // O limite cabe a metade do pedido caprichado, mas não o pedido inteiro.
        var vm = await OpenAsync(folder, Settings(new BudgetLimits(careful * 0.6m, null)));
        Assert.True(vm.IsOverLimit);

        vm.SelectedWhen = vm.Whens[1];

        Assert.False(vm.IsOverLimit);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public async Task Iniciar_na_noite_pede_o_lote_e_a_cena_de_exemplo_nao_tem_a_escolha()
    {
        var vm = await OpenAsync(Project(), Settings());
        TranslationRequest? asked = null;
        vm.StartRequested += (_, request) => asked = request;
        vm.SelectedWhen = vm.Whens[1];

        vm.StartCommand.Execute(null);

        Assert.True(asked!.Overnight);
        Assert.False(vm.IsOpen);

        var sample = await OpenAsync(Project("exemplo"), Settings() with { IsSample = true });
        Assert.False(sample.ShowWhen);
        Assert.False(sample.IsOvernight);
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];

        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

        public void RunUntil(Task task)
        {
            var deadline = Environment.TickCount64 + 30_000;
            while (!task.IsCompleted)
            {
                Assert.True(Environment.TickCount64 < deadline, "A abertura não terminou no prazo.");
                if (queue.TryTake(out var item, 50))
                {
                    SetSynchronizationContext(this);
                    try
                    {
                        item.Callback(item.State);
                    }
                    finally
                    {
                        SetSynchronizationContext(null);
                    }
                }
            }
        }
    }
}
