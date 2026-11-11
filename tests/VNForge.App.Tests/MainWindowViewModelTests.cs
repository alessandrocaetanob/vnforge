using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string? pickerAnswer = null) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(pickerAnswer), new FixedClock(Now)));

    private static (MainWindowViewModel Vm, NewProjectWizardViewModel Wizard) CreateWithWizard(TempFolder temp)
    {
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);
        var vm = new MainWindowViewModel(
            new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)),
            newProjectWizard: () => wizard);
        return (vm, wizard);
    }

    [Fact]
    public void Novo_projeto_troca_a_pagina_pelo_assistente_e_cancelar_volta_ao_Inicio()
    {
        using var temp = new TempFolder();
        var (vm, wizard) = CreateWithWizard(temp);

        vm.Start.NewProjectCommand.Execute(null);
        Assert.Same(wizard, vm.CurrentPage);

        wizard.CancelCommand.Execute(null);
        Assert.Same(vm.Library, vm.CurrentPage);
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
    public async Task Abrir_no_Editor_um_recente_que_sumiu_mostra_mensagem_e_nao_navega()
    {
        using var temp = new TempFolder();
        var gone = SampleProjects.Create(temp.SubFolder("some"), "Some", TranslationStatus.Approved);
        var current = SampleProjects.Create(temp.SubFolder("fica"), "Fica", TranslationStatus.Approved);
        var picker = new FakeFolderPicker(gone);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), picker, new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        var summary = vm.Start.Recents.Single().Summary;
        Assert.NotNull(summary);
        await vm.Start.OpenAsync(current);
        await vm.Editor.Loading;
        Directory.Delete(gone, recursive: true);
        vm.Start.Selected = vm.Start.Recents.Single(r => r.Summary == summary);

        vm.Start.OpenSelectedInEditorCommand.Execute(null);

        Assert.NotNull(vm.Start.ErrorMessage);
        Assert.Same(vm.Library, vm.CurrentPage);
    }

    [Fact]
    public void Sem_fabrica_do_assistente_novo_projeto_nao_troca_a_pagina()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.Start.NewProjectCommand.Execute(null);

        Assert.Same(vm.Library, vm.CurrentPage);
    }
}
