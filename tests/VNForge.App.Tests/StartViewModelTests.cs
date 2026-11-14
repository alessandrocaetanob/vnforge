using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R6")]
[Trait("Risco", "R2")]
public class StartViewModelFalhasTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Falha_ao_gravar_os_recentes_nao_impede_abrir_o_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        // A "pasta de configuração" é um arquivo: criar o recent.json dentro dela dá IOException.
        var bloqueio = Path.Combine(temp.Path, "config-arquivo");
        File.WriteAllText(bloqueio, "x");
        var vm = new StartViewModel(new RecentProjectsFile(bloqueio), new FakeFolderPicker(root), new FixedClock(Now));
        ProjectSummary? aberto = null;
        vm.ProjectOpened += (_, s) => aberto = s;

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal("Minha VN", aberto?.Name);
        Assert.Contains("lista de recentes", vm.NoticeMessage);
        Assert.Null(vm.ErrorMessage);
    }
}
