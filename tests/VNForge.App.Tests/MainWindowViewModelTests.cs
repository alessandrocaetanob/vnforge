using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

[Trait("Risco", "R6")]
public class MainWindowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static (MainWindowViewModel Vm, NewProjectWizardViewModel Wizard) CreateWithWizard(TempFolder temp)
    {
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);
        var vm = new MainWindowViewModel(
            new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)))
        {
            NewProjectWizard = () => wizard,
        };
        return (vm, wizard);
    }

    [Fact]
    public async Task Falha_ao_abrir_o_projeto_criado_vira_mensagem_na_tela_e_nao_derruba_o_processo()
    {
        using var temp = new TempFolder();
        var (vm, wizard) = CreateWithWizard(temp);
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
        vm.Start.NewProjectCommand.Execute(null);
        await wizard.SetGameFolderAsync(game);
        wizard.SetProjectDirectory(Path.Combine(temp.Path, "meu-projeto"));
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        // Um glossário ilegível faz GlossaryViewModel.Reload lançar ProjectConfigException durante SetProject.
        vm.Start.ProjectOpened += (_, _) => throw new ProjectConfigException("glossary.json ilegível.");

        wizard.FinishCommand.Execute(null);
        await vm.ProjectOpening;
        await vm.Editor.Loading;

        Assert.Contains("glossary.json ilegível.", vm.Start.ErrorMessage);
    }

    [Fact]
    public async Task Projeto_criado_pelo_assistente_abre_no_app_e_entra_nos_recentes()
    {
        using var temp = new TempFolder();
        var (vm, wizard) = CreateWithWizard(temp);
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
        vm.Start.NewProjectCommand.Execute(null);
        await wizard.SetGameFolderAsync(game);
        wizard.SetProjectDirectory(Path.Combine(temp.Path, "meu-projeto"));
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);

        wizard.FinishCommand.Execute(null);
        await vm.ProjectOpening;
        await vm.Editor.Loading;

        Assert.Same(vm.Library, vm.CurrentPage);
        Assert.Equal("meu-projeto", vm.ProjectTitle);
        Assert.Single(vm.Start.Recents);
    }
}
