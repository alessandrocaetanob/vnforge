using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

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
    public async Task Pasta_sem_engine_reconhecida_mostra_o_erro_e_nao_deixa_avancar()
    {
        var vm = Create(engineMatches: false);

        await vm.SetGameFolderAsync(game);

        Assert.Contains("Engine não reconhecida", vm.ErrorMessage);
        Assert.False(vm.HasGame);
        Assert.False(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Pasta_sem_executavel_avisa()
    {
        File.Delete(Path.Combine(game, "Jogo.exe"));
        var vm = Create();

        await vm.SetGameFolderAsync(game);

        Assert.Contains("Nenhum executável", vm.ErrorMessage);
        Assert.False(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Escolher_a_pasta_do_jogo_pelo_seletor_roda_a_deteccao()
    {
        var vm = Create(game);

        await vm.BrowseGameCommand.ExecuteAsync(null);

        Assert.True(vm.HasGame);
        Assert.Equal(Path.GetFullPath(game), vm.GameFolder);
        Assert.Equal(Path.GetFullPath(game), vm.GameFolderLabel);
    }

    [Fact]
    public async Task Cancelar_o_seletor_nao_muda_nada()
    {
        var vm = Create();

        await vm.BrowseGameCommand.ExecuteAsync(null);
        await vm.BrowseProjectCommand.ExecuteAsync(null);

        Assert.False(vm.HasGame);
        Assert.Null(vm.ProjectDirectory);
        Assert.Equal("Nenhuma pasta escolhida", vm.ProjectDirectoryLabel);
    }

    [Fact]
    public async Task Escolher_a_pasta_do_projeto_pelo_seletor_define_a_pasta_e_o_nome()
    {
        var vm = Create(ProjectPath("pelo-seletor"));

        await vm.BrowseProjectCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(ProjectPath("pelo-seletor")), vm.ProjectDirectory);
        Assert.Equal("pelo-seletor", vm.ProjectName);
    }

    [Fact]
    public async Task Nome_do_projeto_comeca_no_nome_da_pasta_e_nao_sobrescreve_o_que_o_usuario_digitou()
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);

        vm.SetProjectDirectory(ProjectPath("primeira"));
        Assert.Equal("primeira", vm.ProjectName);
        vm.SetProjectDirectory(ProjectPath("segunda"));
        Assert.Equal("segunda", vm.ProjectName);
        vm.ProjectName = "Minha VN";
        vm.SetProjectDirectory(ProjectPath("terceira"));
        Assert.Equal("Minha VN", vm.ProjectName);
    }

    [Fact]
    public async Task Os_cinco_passos_andam_e_voltam_na_ordem_do_doc_04()
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        Assert.Equal(WizardStep.Game, vm.Step);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.Equal("Passo 1 de 5 · Jogo", vm.StepLabel);

        vm.SetProjectDirectory(ProjectPath());
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.Vndb, vm.Step);
        Assert.True(vm.IsVndbStep);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.Names, vm.Step);
        Assert.True(vm.IsNamesStep);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.Accents, vm.Step);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.Ai, vm.Step);
        Assert.Equal("Criar projeto", vm.NextLabel);

        vm.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Accents, vm.Step);
        Assert.Equal("Avançar", vm.NextLabel);
    }

    [Fact]
    public async Task Passo_do_nome_exige_nome_e_pasta()
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        await AdvanceAsync(vm, 2);
        Assert.False(vm.NextCommand.CanExecute(null));

        vm.ProjectName = "Algo";
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.SetProjectDirectory(ProjectPath());
        vm.ProjectName = " ";
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.ProjectName = "Algo";
        Assert.True(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Projeto_criado_pelo_assistente_ja_tem_os_termos_do_glossario_pessoal()
    {
        var pessoal = new VNForge.Core.Memory.PersonalGlossary(Path.Combine(temp.Path, "config"));
        pessoal.Upsert(new VNForge.Core.Memory.PersonalTerm("お兄ちゃん", "mano"));
        var vm = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine(true)]) { PersonalGlossary = pessoal };
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(ProjectPath());
        await AdvanceAsync(vm, 5);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.Equal("お兄ちゃん", Assert.Single(ProjectFolder.Open(ProjectPath()).LoadGlossary()).Source);
        Assert.Equal("1 termo do glossário pessoal entrou no glossário do projeto.", vm.PersonalNotice);
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

    [Fact]
    public async Task Extracao_que_falha_mostra_o_erro_sem_travar_o_assistente()
    {
        var vm = await AtNamesStepAsync();
        await AdvanceAsync(vm, 3);

        // A engine de mentira não sabe extrair: o erro vem do Core, em português.
        await vm.ExtractCommand.ExecuteAsync(null);

        Assert.False(vm.IsExtracted);
        Assert.Contains("A extração não terminou", vm.ErrorMessage);
        Assert.True(vm.ExtractCommand.CanExecute(null));
    }

    [Fact]
    public async Task Extracao_pela_barra_de_tarefas_cria_a_tarefa_e_reporta_a_falha()
    {
        var tasks = new VNForge.App.Services.BackgroundTaskService();
        var vm = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()], tasks);
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(ProjectPath());
        await AdvanceAsync(vm, 5);

        await vm.ExtractCommand.ExecuteAsync(null);

        var item = Assert.Single(tasks.Tasks);
        Assert.StartsWith("Extrair ", item.Title);
        Assert.Contains("A extração não terminou", vm.ErrorMessage);
        Assert.False(vm.IsExtracted);
    }

    [Theory]
    [InlineData(null, "Amaoto no Hakoniwa", "")]
    [InlineData("", "Amaoto no Hakoniwa", "")]
    [InlineData("雨音の箱庭", "Amaoto no Hakoniwa 雨音の箱庭", " 雨音の箱庭")]
    public void O_jogo_da_Biblioteca_mostra_o_nome_e_o_original_so_quando_o_VNDB_tem(string? original, string titulo, string sufixo)
    {
        var jogo = new LibraryGameContext("Amaoto no Hakoniwa", null, null, original);

        Assert.Equal(titulo, jogo.DisplayTitle);
        Assert.Equal(sufixo, jogo.OriginalSuffix);
    }
}
