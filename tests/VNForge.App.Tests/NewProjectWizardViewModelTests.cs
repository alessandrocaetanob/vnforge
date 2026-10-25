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
        await vm.NextCommand.ExecuteAsync(null);
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
    public async Task Detecta_o_jogo_mostra_engine_versao_e_chave_e_escolhe_o_unico_executavel()
    {
        var vm = Create();

        await vm.SetGameFolderAsync(game);

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.HasGame);
        Assert.Equal("Jogo.exe", vm.SelectedExe);
        Assert.False(vm.HasExecutables);
        Assert.Contains("Engine: YU-RIS · YPF v500", vm.DetectionLines);
        Assert.Contains("Arquivo de scripts: ysbin.ypf (2 scripts)", vm.DetectionLines);
        Assert.Contains("Chave XOR dos scripts: 0xD36FAC96", vm.DetectionLines);
        Assert.True(vm.NextCommand.CanExecute(null));
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
    public async Task Pasta_inexistente_mostra_o_erro()
    {
        var vm = Create();

        await vm.SetGameFolderAsync(Path.Combine(temp.Path, "nada"));

        Assert.Contains("não existe", vm.ErrorMessage);
        Assert.False(vm.HasGame);
    }

    [Fact]
    public async Task Com_varios_executaveis_o_usuario_escolhe_antes_de_avancar()
    {
        File.WriteAllText(Path.Combine(game, "Config.exe"), "MZ");
        var vm = Create();

        await vm.SetGameFolderAsync(game);

        Assert.True(vm.HasExecutables);
        Assert.Equal(["Config.exe", "Jogo.exe"], vm.Executables);
        Assert.Null(vm.SelectedExe);
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.SelectedExe = "Jogo.exe";
        Assert.True(vm.NextCommand.CanExecute(null));
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
    public async Task Os_quatro_passos_andam_e_voltam_na_ordem_do_doc_04()
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        Assert.Equal(WizardStep.Game, vm.Step);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.Equal("Passo 1 de 4 · Pasta do jogo", vm.StepLabel);

        vm.SetProjectDirectory(ProjectPath());
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
        await vm.NextCommand.ExecuteAsync(null);
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
    public async Task Escolher_uma_opcao_desmarca_as_outras()
    {
        var vm = await AtNamesStepAsync();

        vm.EncodingStrategies.Options.Single(o => o.Id == "cp1252").IsSelected = true;

        Assert.Equal("cp1252", vm.EncodingStrategies.Selected.Id);
        Assert.Equal(["cp1252"], vm.EncodingStrategies.Options.Where(o => o.IsSelected).Select(o => o.Id));
    }

    [Fact]
    public void A_opcao_cp1252_diz_que_o_patch_da_fonte_se_aplica_na_tela_Build()
    {
        var description = Create().EncodingStrategies.Options.Single(o => o.Id == "cp1252").Description;

        Assert.Contains("MS Gothic", description);
        Assert.Contains("opcional", description);
        Assert.Contains("tela Testar", description);
        Assert.DoesNotContain("só funciona", description);
    }

    [Fact]
    public void Padroes_sao_os_do_vnf_init()
    {
        var vm = Create();

        Assert.Equal(ProjectChoices.DefaultSourceLanguage, vm.SourceLanguages.Selected.Id);
        Assert.Equal(ProjectChoices.DefaultTargetLanguage, vm.TargetLanguages.Selected.Id);
        Assert.Equal(ProjectChoices.DefaultEncoding, vm.EncodingStrategies.Selected.Id);
        Assert.Equal(ProjectChoices.DefaultAi.Model, vm.Models.Selected.Id);
        Assert.DoesNotContain(vm.EncodingStrategies.Options, o => o.Id == "sjis");
        Assert.Equal(ProjectChoices.Models.Count, vm.Models.Options.Count);
        Assert.All(vm.Models.Options, o => Assert.StartsWith("US$", o.Value));
    }

    [Fact]
    public async Task Pular_a_ia_grava_o_padrao_mesmo_com_outro_modelo_marcado()
    {
        var vm = await AtNamesStepAsync();
        vm.Models.Select("claude-haiku-5-5");
        vm.SkipAi = true;

        Assert.Null(vm.BuildOptions().Ai);
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
    public async Task Projeto_dentro_da_pasta_do_jogo_volta_ao_passo_do_nome_com_o_erro_do_Core()
    {
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(game, "projeto"));

        await AdvanceAsync(vm, 4);

        Assert.Equal(WizardStep.Names, vm.Step);
        Assert.Contains("não pode ficar dentro da pasta do jogo", vm.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(game, "projeto")));
        Assert.Equal(["Jogo.exe"], Directory.EnumerateFileSystemEntries(game).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Pasta_que_ja_tem_projeto_nao_e_sobrescrita()
    {
        SampleProjects.Create(ProjectPath(), "Existente");
        var vm = Create();
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(ProjectPath());

        await AdvanceAsync(vm, 4);

        Assert.Equal(WizardStep.Names, vm.Step);
        Assert.Contains("não foi sobrescrito", vm.ErrorMessage);
        Assert.Equal("Existente", ProjectFolder.Open(ProjectPath()).LoadConfig().Name);
    }

    [Fact]
    public void Cancelar_avisa_quem_abriu_o_assistente()
    {
        var vm = Create();
        var cancelado = 0;
        vm.Cancelled += (_, _) => cancelado++;

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, cancelado);
        Assert.Equal(WizardStep.Game, vm.Step);
    }

    [Fact]
    public async Task Abrir_projeto_no_fim_entrega_a_pasta_criada()
    {
        var vm = await AtNamesStepAsync();
        await AdvanceAsync(vm, 3);

        string? aberta = null;
        vm.Completed += (_, root) => aberta = root;
        vm.FinishCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(ProjectPath()), aberta);
    }

    [Fact]
    public void Finish_sem_projeto_criado_nao_faz_nada()
    {
        var vm = Create();
        var chamado = false;
        vm.Completed += (_, _) => chamado = true;

        vm.FinishCommand.Execute(null);

        Assert.False(chamado);
        Assert.False(vm.ExtractCommand.CanExecute(null));
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
        await AdvanceAsync(vm, 4);

        await vm.ExtractCommand.ExecuteAsync(null);

        var item = Assert.Single(tasks.Tasks);
        Assert.StartsWith("Extrair ", item.Title);
        Assert.Contains("A extração não terminou", vm.ErrorMessage);
        Assert.False(vm.IsExtracted);
    }
}
