using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class BuildViewModelTests : IDisposable
{
    private const string Ysbin = "ysbin.ypf";
    private const string Original = "original";
    private const string Traduzido = "traduzido";

    private readonly TempFolder temp = new();
    private readonly string game;
    private readonly ProjectFolder folder;
    private readonly RecordingStarter starter = new();

    public BuildViewModelTests()
    {
        game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, Ysbin), Original);
        File.WriteAllText(Path.Combine(game, "jogo.exe"), "MZ");
        folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => temp.Dispose();

    private string GameFile => Path.Combine(game, Ysbin);

    private void PutBuild(string name = Ysbin, string content = Traduzido)
    {
        var path = Path.Combine(folder.BuildDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<BuildViewModel> OpenAsync()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], starter);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        return vm;
    }

    [Fact]
    public async Task Sem_build_nada_pode_ser_injetado_nem_restaurado()
    {
        var vm = await OpenAsync();

        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.False(vm.RestoreCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.Contains("Ainda não há build", vm.PlanMessage);
        Assert.Equal(game, vm.GameFolder);
        Assert.Contains("Nenhum backup", vm.BackupMessage);
        Assert.Equal(folder.BackupDirectory, vm.BackupLocation);
    }

    [Fact]
    public async Task Sem_projeto_nenhum_comando_roda()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [], starter);

        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.False(vm.RestoreCommand.CanExecute(null));
        Assert.False(vm.LaunchCommand.CanExecute(null));
        await vm.ReloadAsync();
    }

    [Fact]
    public async Task Com_build_lista_o_que_muda_no_jogo_sem_gravar_nada()
    {
        PutBuild();
        PutBuild("pac/extra.ypf", "novo");

        var vm = await OpenAsync();

        Assert.True(vm.InjectCommand.CanExecute(null));
        Assert.Equal(2, vm.Changes.Count);
        Assert.Contains(vm.Changes, c => c.Path == Ysbin && c.Kind == "Substitui o original");
        Assert.Contains(vm.Changes, c => c.Path == "pac/extra.ypf" && c.Kind == "Arquivo novo");
        Assert.Contains("2 arquivo(s)", vm.PlanMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Build_igual_ao_jogo_nao_tem_o_que_injetar()
    {
        PutBuild(content: Original);

        var vm = await OpenAsync();

        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.Empty(vm.Changes);
        Assert.Contains("já estão iguais", vm.PlanMessage);
    }

    [Fact]
    public async Task Injetar_pede_confirmacao_e_so_grava_depois_de_confirmar()
    {
        PutBuild();
        var vm = await OpenAsync();

        vm.InjectCommand.Execute(null);

        Assert.True(vm.IsConfirming);
        Assert.Contains("1 arquivo(s)", vm.ConfirmationText);
        Assert.Equal(Original, File.ReadAllText(GameFile));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(vm.IsConfirming);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
        var backup = Assert.Single(Directory.GetDirectories(folder.BackupDirectory));
        Assert.Equal(Original, File.ReadAllText(Path.Combine(backup, Ysbin)));
        Assert.Equal(backup, vm.BackupLocation);
        Assert.Contains(vm.Log, l => l.StartsWith("Backup: ", StringComparison.Ordinal));
        Assert.Contains(vm.Log, l => l.Contains("Injetado: 1 arquivo(s)", StringComparison.Ordinal));
        Assert.True(vm.RestoreCommand.CanExecute(null));
        Assert.False(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelar_a_confirmacao_nao_grava_nada()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);

        vm.CancelConfirmationCommand.Execute(null);

        Assert.False(vm.IsConfirming);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.False(Directory.Exists(folder.BackupDirectory) && Directory.GetDirectories(folder.BackupDirectory).Length > 0);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_pede_confirmacao_e_devolve_o_original()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));

        vm.RestoreCommand.Execute(null);

        Assert.True(vm.IsConfirming);
        Assert.Contains("Restaurar", vm.ConfirmationText);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.Contains(vm.Log, l => l.Contains("Restaurado: 1 arquivo(s)", StringComparison.Ordinal));
        Assert.True(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_remove_o_arquivo_que_a_injecao_criou()
    {
        PutBuild("pac/extra.ypf", "novo");
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        var extra = Path.Combine(game, "pac", "extra.ypf");
        Assert.True(File.Exists(extra));

        vm.RestoreCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(File.Exists(extra));
        Assert.Contains(vm.Log, l => l.Contains("removido: 1 arquivo(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_da_injecao_vira_mensagem_e_o_jogo_fica_como_estava()
    {
        PutBuild();
        var vm = await OpenAsync();
        File.WriteAllText(GameFile, "mudou depois do plano");

        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal("mudou depois do plano", File.ReadAllText(GameFile));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Abrir_jogo_dispara_o_executavel_do_projeto()
    {
        var vm = await OpenAsync();

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(game, "jogo.exe"), starter.Executable);
        Assert.Equal(game, starter.WorkingDirectory);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(vm.Log, l => l.StartsWith("Jogo aberto: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Abrir_jogo_sem_o_executavel_mostra_o_caminho_procurado()
    {
        File.Delete(Path.Combine(game, "jogo.exe"));
        var vm = await OpenAsync();

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Null(starter.Executable);
        Assert.Contains("jogo.exe", vm.ErrorMessage);
    }

    [Fact]
    public async Task Build_com_engine_sem_suporte_mostra_o_erro_sem_derrubar_o_app()
    {
        var vm = await OpenAsync();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.Contains(vm.Log, l => l.Contains("Build falhou", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_o_log_e_o_estado()
    {
        PutBuild();
        var vm = await OpenAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Log);

        var other = SampleProjects.Create(temp.SubFolder("outro"), "Outro", TranslationStatus.Approved);
        vm.Project = ProjectSummaryReader.Read(other);
        await vm.ReloadAsync();

        Assert.Empty(vm.Log);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsConfirming);
    }

    private sealed class RecordingStarter : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public void Start(string executablePath, string workingDirectory)
        {
            Executable = executablePath;
            WorkingDirectory = workingDirectory;
        }
    }
}
