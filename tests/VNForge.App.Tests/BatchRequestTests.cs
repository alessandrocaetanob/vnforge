using Microsoft.Extensions.Time.Testing;
using VNForge.AI.BatchRequest;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// O pedido em lote escrito em português: a paleta oferece o comando, o painel de busca e substituição mostra as falas e o custo, a IA só é chamada ao
/// confirmar, a prévia traz antes e depois e nada é gravado até Aplicar, que passa pelo ponto de restauração.
/// </summary>
[Trait("Risco", "R5")]
public sealed class BatchRequestTests : IDisposable
{
    private const string Request = "nas falas do Kenta, trocar você por tu";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder temp = new();
    private readonly FakeTimeProvider clock = new(Now);
    private readonly string root;

    public BatchRequestTests()
    {
        root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [("yst00001", TranslationStatus.Translated)]);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("健太", "Kenta"), new CharacterEntry("美咲", "Misaki")]);
        folder.SaveText("yst00001",
        [
            Line(0, "健太", "Você sabe o que houve?", TranslationStatus.Approved),
            Line(1, "美咲", "Você chegou cedo.", TranslationStatus.Machine),
            Line(2, "健太", "Vamos embora.", TranslationStatus.Machine),
            Line(3, "健太", "Eu vi você ontem.", TranslationStatus.Machine),
        ]);
    }

    public void Dispose() => temp.Dispose();

    private sealed class ThrowingProvider : IBatchRequestProvider
    {
        public Task<IReadOnlyList<BatchRewrite>> RewriteAsync(string request, VNForge.AI.Translation.TranslationBatch batch, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<BatchRewrite>>(new InvalidOperationException("sem rede"));
    }

    private static TextUnit Line(int index, string speaker, string translation, TranslationStatus status) =>
        new($"yst00001:{index:D6}", TextUnitKind.Dialogue, speaker, $"原文{index}", translation, status);

    private static BatchRequestSettings Settings(bool key = true, BudgetLimits? limits = null) =>
        new(ExchangeRate.Placeholder, limits ?? BudgetLimits.None, TranslationQuality.Balanced, 2, key, (_, _) => Task.CompletedTask);

    private (SearchReplaceViewModel Vm, FakeBatchRequestProvider Provider, BackgroundTaskService Tasks) Build(bool key = true, BudgetLimits? limits = null, Func<IBatchRequestProvider, ClaudeUsage?>? usageOf = null)
    {
        var provider = FakeBatchRequestProvider.Replacing("você", "tu");
        var tasks = new BackgroundTaskService(time: clock);
        var vm = new SearchReplaceViewModel(clock)
        {
            Project = ProjectSummaryReader.Read(root),
            Requests = new BatchRequestService((_, _) => provider, () => Settings(key, limits), usageOf),
            History = new BatchHistory(tasks, clock),
        };
        return (vm, provider, tasks);
    }

    [Fact]
    public async Task Sem_chave_o_pedido_e_recusado_antes_de_qualquer_gasto()
    {
        var (vm, provider, _) = Build(key: false);

        await vm.BeginRequestAsync(Request);

        Assert.False(vm.HasRequestPlan);
        Assert.Contains("falta a chave", vm.ErrorMessage);
        Assert.Empty(provider.Received);
    }

    [Fact]
    public async Task Limite_de_gasto_atingido_recusa_o_pedido()
    {
        var folder = ProjectFolder.Open(root);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(1_000_000, 1_000_000, 0, 0, 100m, 1, 0));
        var (vm, provider, _) = Build(limits: new BudgetLimits(1m, null));

        await vm.BeginRequestAsync(Request);

        Assert.False(vm.HasRequestPlan);
        Assert.Contains("limite de gasto", vm.ErrorMessage);
        Assert.Empty(provider.Received);
    }

    [Fact]
    public async Task O_gasto_da_IA_fica_registrado_no_projeto()
    {
        var (vm, _, _) = Build(usageOf: _ => new ClaudeUsage(2_000, 500, 0, 0));
        await vm.BeginRequestAsync(Request);

        await vm.RunRequestCommand.ExecuteAsync(null);

        Assert.True(AiUsageLog.Load(ProjectFolder.Open(root)).Dollars > 0m);
        Assert.Contains("Gasto: ", vm.Summary);
    }

    private sealed class GatedProvider(Task gate) : IBatchRequestProvider
    {
        public async Task<IReadOnlyList<BatchRewrite>> RewriteAsync(string request, VNForge.AI.Translation.TranslationBatch batch, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            return [.. batch.Lines.Select(l => new BatchRewrite(l.Id, l.Translation!.Replace("você", "tu", StringComparison.Ordinal)))];
        }
    }
}
