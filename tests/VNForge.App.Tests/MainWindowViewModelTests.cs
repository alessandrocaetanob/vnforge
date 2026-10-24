using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string? pickerAnswer = null) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(pickerAnswer), new FixedClock(Now)));

    [Fact]
    public void Abre_no_Inicio_com_o_Editor_desabilitado_e_sem_projeto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.Equal("Início", vm.SelectedNav?.Label);
        Assert.IsType<StartViewModel>(vm.CurrentPage);
        Assert.False(vm.Nav.Single(n => n.Key == NavKey.Editor).IsEnabled);
        Assert.Null(vm.CurrentProject);
        Assert.Equal("Nenhum projeto aberto", vm.ProjectTitle);
    }

    [Fact]
    public async Task Abrir_projeto_habilita_o_Editor_e_mostra_o_nome_na_barra_de_titulo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal("Minha VN", vm.ProjectTitle);
        Assert.True(vm.Nav.Single(n => n.Key == NavKey.Editor).IsEnabled);
        Assert.IsType<StartViewModel>(vm.CurrentPage);
    }

    [Fact]
    public async Task Pedir_o_Editor_navega_e_entrega_o_projeto_ao_Editor()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);

        vm.Start.OpenSelectedInEditorCommand.Execute(null);

        var editor = Assert.IsType<EditorViewModel>(vm.CurrentPage);
        Assert.Equal("Minha VN", editor.Project?.Name);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
    }

    [Fact]
    public void Selecionar_item_desabilitado_do_trilho_nao_navega()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);

        Assert.IsType<StartViewModel>(vm.CurrentPage);
        Assert.Equal(NavKey.Start, vm.SelectedNav?.Key);
    }

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
        Assert.Same(vm.Start, vm.CurrentPage);
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

        wizard.FinishCommand.Execute(null);
        // O handler de Completed abre o projeto em segundo plano; espera o Início registrar.
        for (var i = 0; i < 100 && vm.CurrentProject is null; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Same(vm.Start, vm.CurrentPage);
        Assert.Equal("meu-projeto", vm.ProjectTitle);
        Assert.Single(vm.Start.Recents);
    }

    [Fact]
    public void Sem_fabrica_do_assistente_novo_projeto_nao_troca_a_pagina()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.Start.NewProjectCommand.Execute(null);

        Assert.Same(vm.Start, vm.CurrentPage);
    }
}
