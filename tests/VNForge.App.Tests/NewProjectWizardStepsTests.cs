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
    public void Sao_quatro_passos_numerados_e_o_primeiro_e_o_atual()
    {
        var vm = Create();

        Assert.Equal([1, 2, 3, 4], vm.Steps.Select(s => s.Number));
        Assert.Equal(["Pasta do jogo", "Idiomas e nome", "Acentos", "IA e gasto"], vm.Steps.Select(s => s.Title));
        Assert.Equal([PipelineStepState.Active, PipelineStepState.Pending, PipelineStepState.Pending, PipelineStepState.Pending], vm.Steps.Select(s => s.State));
    }

    [Fact]
    public void Cada_passo_pendente_diz_o_que_falta()
    {
        var vm = Create();

        Assert.Equal(
            ["Escolha a pasta instalada", "Nome, pasta e idiomas", "Como ficam os acentos", "Qualidade e limite de gasto"],
            vm.Steps.Select(s => s.Subtitle));
    }

    [Fact]
    public async Task Pasta_detectada_mostra_a_engine_como_resultado()
    {
        var vm = Create();
        Assert.False(vm.HasDetection);

        await vm.SetGameFolderAsync(game);

        Assert.True(vm.HasDetection);
        Assert.Equal("YU-RIS · YPF v500", vm.EngineTitle);
        Assert.Equal("Feito com o motor YU-RIS", vm.Steps[0].Subtitle);
        Assert.Equal(
            [("Arquivo de scripts", "ysbin.ypf · 2 scripts"), ("Chave XOR dos scripts", "0xD36FAC96"), ("Página de código", "932 (Shift-JIS)")],
            vm.DetectionFacts.Select(f => (f.Label, f.Value)));
    }

    [Fact]
    public async Task Pasta_sem_engine_nao_mostra_resultado()
    {
        var vm = Create(engineMatches: false);

        await vm.SetGameFolderAsync(game);

        Assert.False(vm.HasDetection);
        Assert.Empty(vm.DetectionFacts);
        Assert.Equal("Escolha a pasta instalada", vm.Steps[0].Subtitle);
    }

    [Fact]
    public async Task Avancar_conclui_o_passo_e_resume_a_escolha_nele()
    {
        var vm = await AtStepAsync(WizardStep.Accents);

        Assert.Equal([PipelineStepState.Done, PipelineStepState.Done, PipelineStepState.Active, PipelineStepState.Pending], vm.Steps.Select(s => s.State));
        Assert.Equal("Japonês → Português do Brasil", vm.Steps[1].Subtitle);
        Assert.Equal("Feito com o motor YU-RIS", vm.Steps[0].Subtitle);
    }

    [Fact]
    public async Task Trocar_o_idioma_ou_a_acentuacao_muda_o_resumo()
    {
        var vm = await AtStepAsync(WizardStep.Ai);
        vm.TargetLanguages.Select("es");
        vm.SourceLanguages.Select("en");
        vm.EncodingStrategies.Select("cp1252");

        Assert.Equal("Inglês → Espanhol", vm.Steps[1].Subtitle);
        Assert.Equal("Com acentos, ajuste no jogo", vm.Steps[2].Subtitle);
    }

    [Fact]
    public async Task O_resumo_da_ia_diz_a_qualidade_escolhida()
    {
        var vm = await AtStepAsync(WizardStep.Ai);
        vm.Models.Select("claude-sonnet-5-5");
        Assert.Equal("Qualidade e limite de gasto", vm.Steps[3].Subtitle);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.All(vm.Steps, s => Assert.Equal(PipelineStepState.Done, s.State));
        Assert.Equal("Equilibrada", vm.Steps[3].Subtitle);
    }

    [Fact]
    public async Task Passo_concluido_volta_para_ele_e_o_pendente_nao_tem_comando()
    {
        var vm = await AtStepAsync(WizardStep.Accents);

        Assert.NotNull(vm.Steps[0].Command);
        Assert.NotNull(vm.Steps[1].Command);
        Assert.Null(vm.Steps[2].Command);
        Assert.Null(vm.Steps[3].Command);

        vm.Steps[0].Command!.Execute(vm.Steps[0].CommandParameter);

        Assert.Equal(WizardStep.Game, vm.Step);
        Assert.Equal(PipelineStepState.Active, vm.Steps[0].State);
        Assert.Equal(PipelineStepState.Pending, vm.Steps[1].State);
    }

    [Fact]
    public async Task Voltar_pelo_passo_nao_apaga_as_escolhas_feitas()
    {
        var vm = await AtStepAsync(WizardStep.Accents);
        vm.TargetLanguages.Select("en");

        vm.GoToStepCommand.Execute(WizardStep.Names);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal("Japonês → Inglês", vm.Steps[1].Subtitle);
    }

    [Fact]
    public async Task A_migalha_conta_os_passos_e_diz_pronto_no_fim()
    {
        var vm = await AtStepAsync(WizardStep.Names);
        Assert.Equal("Passo 2 de 4", vm.Breadcrumb);

        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.Equal("Pronto", vm.Breadcrumb);
    }

    [Fact]
    public async Task Nao_da_para_pular_para_um_passo_adiante()
    {
        var vm = await AtStepAsync(WizardStep.Names);

        Assert.False(vm.GoToStepCommand.CanExecute(WizardStep.Ai));
        Assert.False(vm.GoToStepCommand.CanExecute(WizardStep.Names));
        Assert.True(vm.GoToStepCommand.CanExecute(WizardStep.Game));
    }
}
