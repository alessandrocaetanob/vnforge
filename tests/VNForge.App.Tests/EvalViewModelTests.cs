using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
public sealed class EvalViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeJudgeProvider _judge = new(unit => unit.Translation!.StartsWith("tradução", StringComparison.Ordinal) ? 9 : 4);
    private readonly List<string> _models = [];

    public void Dispose() => _temp.Dispose();

    private ProjectFolder Project(int approved = 30, int untranslated = 20)
    {
        var statuses = Enumerable.Repeat(TranslationStatus.Approved, approved).Concat(Enumerable.Repeat(TranslationStatus.Untranslated, untranslated)).ToArray();
        return ProjectFolder.Open(SampleProjects.Create(_temp.SubFolder($"vn{Guid.NewGuid():N}"), "Minha VN", statuses));
    }

    private EvalViewModel Vm(Func<DashboardContext>? context = null) => new()
    {
        Context = context ?? (() => DashboardContext.Default),
        TranslationProviderFactory = (model, _) =>
        {
            _models.Add(model);
            return model == ClaudeModels.Opus ? new FakeTranslationProvider(u => $"tradução {u.Source[2..]}") : new FakeTranslationProvider(u => $"[PT] {u.Source}");
        },
        JudgeProviderFactory = _ => _judge,
    };

    [Fact]
    public async Task Sem_chave_ou_acima_do_limite_o_botao_de_rodar_fica_desligado()
    {
        var noKey = Vm(() => new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false));
        await noKey.LoadAsync(Project());
        await noKey.RunOpenCommand.ExecuteAsync(null);
        Assert.True(noKey.KeyMissing);
        Assert.False(noKey.CanRun);

        var tight = Vm(() => new DashboardContext(ExchangeRate.Placeholder, new BudgetLimits(0.001m, null), HasApiKey: true));
        await tight.LoadAsync(Project());
        await tight.RunOpenCommand.ExecuteAsync(null);
        Assert.True(tight.IsOverLimit);
        Assert.False(tight.CanRun);
        Assert.Contains("limite de gasto", tight.OverLimitText);
    }

}
