using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O Painel lendo um projeto de verdade em disco: os números do anel, a jornada, o gasto e a troca de projeto.</summary>
public class DashboardViewModelTests
{
    private static DashboardViewModel Open(TempFolder temp, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        return new DashboardViewModel { Project = ProjectSummaryReader.Read(root) };
    }

    [Fact]
    public async Task O_gasto_vem_do_registro_do_projeto_em_reais_pela_cotacao_dos_ajustes()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        vm.Context = () => new DashboardContext(new VNForge.AI.Cost.ExchangeRate(5.40m, new DateOnly(2026, 10, 22)), new VNForge.AI.Cost.BudgetLimits(300m, null), true);
        await vm.ReloadAsync();
        Assert.Equal("R$ 0,00 de R$ 300,00", vm.SpentText);

        AiUsageLog.TryRecord(ProjectFolder.Open(vm.Project!.Root), new AiUsageTotals(1_500_000, 600_000, 0, 0, 26.40m, 1));
        await vm.ReloadAsync();

        Assert.Equal("R$ 142,56 de R$ 300,00", vm.SpentText);
        Assert.Equal(142.56, vm.SpentBrl, 2);
        Assert.Equal(300, vm.LimitBrl);
        Assert.Equal("Faltam R$ 157,44 para o limite.", vm.SpendHint);
    }

    [Fact]
    public async Task Sem_projeto_a_tela_fica_vazia()
    {
        var vm = new DashboardViewModel();

        await vm.ReloadAsync();

        Assert.False(vm.HasProject);
        Assert.Empty(vm.Steps);
        Assert.Equal(string.Empty, vm.SpentText);
        Assert.True(vm.ShowEmptyState);
    }
}
