using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

[Trait("Risco", "R2")]
[Trait("Risco", "R6")]
public sealed class NewProjectWizardViewModelTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string game;

    public NewProjectWizardViewModelTests()
    {
        game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
    }

    public void Dispose() => temp.Dispose();

    private string ProjectPath(string name = "minha-vn") => Path.Combine(temp.Path, name);

    private static NewProjectWizardViewModel Create(string? pickerAnswer = null, bool engineMatches = true) =>
        new(new FakeFolderPicker(pickerAnswer), [new FakeEngine(engineMatches)]);

    private async Task<NewProjectWizardViewModel> AtNamesStepAsync()
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(ProjectPath());
        await AdvanceAsync(vm, 2);
        return vm;
    }

    private static async Task AdvanceAsync(NewProjectWizardViewModel vm, int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            await vm.NextCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task Criar_grava_o_vnforge_json_com_as_escolhas_e_vai_para_o_passo_final()
    {
        var vm = await AtNamesStepAsync();
        vm.ProjectName = "Minha VN — PT-BR";
        vm.SourceLanguages.Select("en");
        vm.TargetLanguages.Select("es");
        await vm.NextCommand.ExecuteAsync(null);
        vm.EncodingStrategies.Select("cp1252");
        await vm.NextCommand.ExecuteAsync(null);
        vm.Models.Select("claude-sonnet-5-5");

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.Null(vm.ErrorMessage);
        var config = ProjectFolder.Open(ProjectPath()).LoadConfig();
        Assert.Equal("Minha VN — PT-BR", config.Name);
        Assert.Equal(new LanguagesConfig("en", "es"), config.Languages);
        Assert.Equal(new EncodingConfig("cp1252"), config.Encoding);
        Assert.Equal("claude-sonnet-5-5", config.Ai.Model);
        Assert.Equal("Jogo.exe", config.Game.Exe);
        Assert.Equal(new EngineConfig("yuris", 500, "0xD36FAC96"), config.Engine);
        Assert.Contains("Minha VN — PT-BR", vm.CreatedSummary);
    }

    [Fact]
    public async Task Pasta_que_ja_tem_projeto_nao_e_sobrescrita()
    {
        SampleProjects.Create(ProjectPath(), "Existente");
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(ProjectPath());

        await AdvanceAsync(vm, 5);

        Assert.Equal(WizardStep.Names, vm.Step);
        Assert.Contains("não foi sobrescrito", vm.ErrorMessage);
        Assert.Equal("Existente", ProjectFolder.Open(ProjectPath()).LoadConfig().Name);
    }

}
