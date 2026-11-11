using VNForge.App.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Tests;

/// <summary>Os passos numerados do assistente (no estilo do PipelineStepper) e a engine detectada como resultado (#248).</summary>
public sealed class NewProjectWizardStepsTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string game;

    public NewProjectWizardStepsTests()
    {
        game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
    }

    public void Dispose() => temp.Dispose();

    private static NewProjectWizardViewModel Create(bool engineMatches = true) =>
        new(new FakeFolderPicker(null), [new FakeEngine(engineMatches)]);

    private async Task<NewProjectWizardViewModel> AtStepAsync(WizardStep target)
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        while (vm.Step < target)
        {
            await vm.NextCommand.ExecuteAsync(null);
        }

        return vm;
    }

    [Fact]
    public async Task Passo_concluido_volta_para_ele_e_o_pendente_nao_tem_comando()
    {
        var vm = await AtStepAsync(WizardStep.Accents);

        Assert.NotNull(vm.Steps[0].Command);
        Assert.NotNull(vm.Steps[1].Command);
        Assert.NotNull(vm.Steps[2].Command);
        Assert.Null(vm.Steps[3].Command);
        Assert.Null(vm.Steps[4].Command);

        vm.Steps[0].Command!.Execute(vm.Steps[0].CommandParameter);

        Assert.Equal(WizardStep.Game, vm.Step);
        Assert.Equal(PipelineStepState.Active, vm.Steps[0].State);
        Assert.Equal(PipelineStepState.Pending, vm.Steps[2].State);
    }

    [Fact]
    public async Task A_migalha_conta_os_passos_e_diz_pronto_no_fim()
    {
        var vm = await AtStepAsync(WizardStep.Names);
        Assert.Equal("Passo 3 de 5", vm.Breadcrumb);

        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.Equal("Pronto", vm.Breadcrumb);
    }

    [Fact]
    public async Task A_legenda_da_ia_continua_a_mesma_depois_de_concluir()
    {
        var vm = await AtStepAsync(WizardStep.Ai);
        vm.Models.Select("claude-sonnet-5-5");
        Assert.Equal("Qualidade e limite de gasto", vm.Steps[4].Subtitle);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.All(vm.Steps, s => Assert.Equal(PipelineStepState.Done, s.State));
        Assert.Equal("Qualidade e limite de gasto", vm.Steps[4].Subtitle);
    }
}
