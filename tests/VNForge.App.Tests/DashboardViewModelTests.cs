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
    public async Task Projeto_ilegivel_mostra_o_erro_e_limpa_a_tela()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        await vm.ReloadAsync();
        File.WriteAllText(Path.Combine(vm.Project!.Root, "text", "yst00001.json"), "{ quebrado");

        await vm.ReloadAsync();

        Assert.StartsWith("Não foi possível ler o projeto", vm.ErrorMessage);
        Assert.Empty(vm.Chapters);
        Assert.Empty(vm.Steps);
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
