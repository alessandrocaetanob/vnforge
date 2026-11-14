using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.App.ViewModels;
using VNForge.Core.Bible;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
public sealed partial class BibleViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _root;
    private readonly ProjectFolder _project;

    public BibleViewModelTests()
    {
        _root = SampleProjects.Create(_temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        _project = ProjectFolder.Open(_root);
        _project.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, "美咲", "ただいま", null, TranslationStatus.Untranslated)]);
        _project.SaveText("yst00010", [new TextUnit("yst00010:000000", TextUnitKind.Dialogue, "美咲", "さようなら", null, TranslationStatus.Untranslated)]);
    }

    public void Dispose() => _temp.Dispose();

    private sealed class StubClient(Func<ClaudeReply> reply) : IClaudeMessageClient
    {
        public int Calls { get; private set; }

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(reply());
        }
    }

    private BibleViewModel OpenWith(StubClient client, BudgetLimits? limits = null)
    {
        var provider = new ClaudeBibleProvider(client);
        var vm = new BibleViewModel { ProviderFactory = (_, _) => provider };
        if (limits is not null)
        {
            vm.Limits = () => limits;
        }

        vm.Open(ProjectSummaryReader.Read(_root));
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");
        return vm;
    }

    [Fact]
    public async Task Reescrever_recusa_o_pedido_que_nao_cabe_no_limite_e_nao_chama_a_IA()
    {
        var client = new StubClient(() => throw new InvalidOperationException("não devia chamar"));
        var vm = OpenWith(client, new BudgetLimits(0.0001m, null));

        await vm.RewriteFieldCommand.ExecuteAsync(BibleField.Tone);

        Assert.Equal(0, client.Calls);
        Assert.Contains("limite", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0m, AiUsageLog.Load(_project).Dollars);
    }

    [Fact]
    public async Task Gerar_recusa_a_estimativa_que_nao_cabe_no_limite_e_nao_chama_a_IA()
    {
        var client = new StubClient(() => throw new InvalidOperationException("não devia chamar"));
        var vm = OpenWith(client, new BudgetLimits(0.0001m, null));
        vm.OpenGenerateCommand.Execute(null);
        await vm.Generation;

        vm.ConfirmGenerateCommand.Execute(null);
        await vm.Generation;

        Assert.Equal(0, client.Calls);
        Assert.Contains("limite", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.IsRunning);
        Assert.False(_project.LoadBible().Scenes.Any());
    }
}
