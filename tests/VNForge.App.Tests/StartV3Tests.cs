using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>O Início da v3: a parte pronta da lista, o próximo passo do Painel, a pasta solta que abre o Novo projeto e a barra de status.</summary>
[Trait("Risco", "R6")]
public class StartV3Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StartViewModel Create(TempFolder temp, string? pickerAnswer = null) =>
        new(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(pickerAnswer), new FixedClock(Now));

    [Fact]
    public async Task A_pasta_solta_abre_o_assistente_com_a_pasta_preenchida()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);
        var vm = new MainWindowViewModel(Create(temp)) { NewProjectWizard = () => wizard };

        await vm.Start.DropFolderAsync(game);

        Assert.Same(wizard, vm.CurrentPage);
        await vm.WizardPreparing;
        Assert.Equal(Path.GetFullPath(game), wizard.GameFolder);
    }
}
