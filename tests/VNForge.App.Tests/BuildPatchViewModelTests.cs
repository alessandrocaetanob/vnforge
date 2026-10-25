using System.Text;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;

namespace VNForge.App.Tests;

/// <summary>
/// Patch da fonte no executável pela tela Build (#299), sobre um <c>.exe</c> sintético gerado aqui: nenhum arquivo de jogo entra no repositório.
/// O App só chama o Core (<c>GameInjector.PlanPatch</c>, <c>InjectPatch</c>, <c>DiscardPatch</c>) com o patch da engine.
/// </summary>
public sealed class BuildPatchViewModelTests : IDisposable
{
    private const string Exe = "jogo.exe";
    private const int NameOffset = 0x300;

    /// <summary><c>ＭＳ ゴシック</c> em Shift-JIS, 13 bytes.</summary>
    private static readonly byte[] JapaneseName = [0x82, 0x6C, 0x82, 0x72, 0x20, 0x83, 0x53, 0x83, 0x56, 0x83, 0x62, 0x83, 0x4E];

    private readonly TempFolder temp = new();
    private readonly string game;
    private readonly string projectRoot;

    public BuildPatchViewModelTests()
    {
        game = temp.SubFolder("jogo");
        projectRoot = temp.SubFolder("projeto");
        File.WriteAllBytes(ExePath, SyntheticExe());
    }

    public void Dispose() => temp.Dispose();

    private string ExePath => Path.Combine(game, Exe);

    private ProjectFolder Folder => ProjectFolder.Open(projectRoot);

    /// <summary>Executável de mentira: cabeçalho <c>MZ</c>, enchimento, o nome japonês com terminador e o mesmo nome em UTF-16 (a caixa de diálogo).</summary>
    private static byte[] SyntheticExe()
    {
        var bytes = new byte[0x1000];
        Array.Fill(bytes, (byte)0xCC);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        Array.Clear(bytes, NameOffset - 1, 0x20);
        JapaneseName.CopyTo(bytes, NameOffset);
        Encoding.Unicode.GetBytes("ＭＳ ゴシック").CopyTo(bytes, 0x800);
        return bytes;
    }

    private static byte[] Patched(byte[] original)
    {
        var copy = (byte[])original.Clone();
        Array.Clear(copy, NameOffset, JapaneseName.Length);
        "MS Gothic"u8.CopyTo(copy.AsSpan(NameOffset));
        return copy;
    }

    private async Task<BuildViewModel> OpenAsync(string strategy = "cp1252", bool withPatchEngine = true)
    {
        ProjectFolder.Create(projectRoot, new ProjectConfig(
            "Teste",
            new GameConfig(game, Exe),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig(strategy),
            AiConfig.Default)).SaveState(new PipelineState([], new Dictionary<string, StageState>(), game));
        IReadOnlyList<VNForge.Core.Engines.IEngine> engines = withPatchEngine ? [new YuRisEngine()] : [new FakeEngine()];
        var vm = new BuildViewModel(new BackgroundTaskService(), engines, new NoStarter());
        vm.Project = ProjectSummaryReader.Read(projectRoot);
        await vm.ReloadAsync();
        return vm;
    }

    private string PatchStaging => Path.Combine(Folder.CacheDirectory, "patch");

    /// <summary>Arquivos que sobraram na cópia de trabalho do patch (<c>.cache/patch</c>); depois de aplicar, cancelar ou ler o estado, nenhum.</summary>
    private string[] StagedFiles() =>
        Directory.Exists(PatchStaging) ? Directory.GetFiles(PatchStaging, "*", SearchOption.AllDirectories) : [];

    private bool HasBackups => Directory.Exists(Folder.BackupDirectory) && Directory.EnumerateDirectories(Folder.BackupDirectory).Any();

    [Fact]
    public async Task Projeto_cp1252_com_exe_em_shift_jis_oferece_o_patch_sem_gravar_nada()
    {
        var original = File.ReadAllBytes(ExePath);

        var vm = await OpenAsync();

        Assert.True(vm.HasPatch);
        Assert.Equal(BuildPatchState.Available, vm.PatchState);
        Assert.Contains("MS Gothic", vm.PatchText);
        Assert.True(vm.PatchCommand.CanExecute(null));
        Assert.Equal(original, File.ReadAllBytes(ExePath));
        Assert.False(HasBackups);
        Assert.Empty(StagedFiles());
    }

    [Theory]
    [InlineData("fold")]
    [InlineData("remap")]
    [InlineData("sjis")]
    public async Task Outra_acentuacao_nao_mostra_o_patch(string strategy)
    {
        var vm = await OpenAsync(strategy);

        Assert.False(vm.HasPatch);
        Assert.Equal(BuildPatchState.None, vm.PatchState);
        Assert.False(vm.PatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Engine_sem_patch_de_executavel_nao_mostra_o_patch()
    {
        var vm = await OpenAsync(withPatchEngine: false);

        Assert.False(vm.HasPatch);
        Assert.False(vm.PatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Aplicar_pede_confirmacao_e_so_grava_depois_de_confirmar_com_backup_do_original()
    {
        var original = File.ReadAllBytes(ExePath);
        var vm = await OpenAsync();

        await vm.PatchCommand.ExecuteAsync(null);

        Assert.True(vm.IsConfirming);
        Assert.Equal(BuildConfirmation.Patch, vm.Pending);
        var file = Assert.Single(vm.ConfirmationFiles);
        Assert.Equal(Exe, file.Path);
        Assert.Equal("Substitui o original", file.Kind);
        Assert.Contains("1 arquivo(s)", vm.ConfirmationText);
        Assert.Contains(Folder.BackupDirectory, vm.ConfirmationText);
        Assert.Equal(original, File.ReadAllBytes(ExePath));
        Assert.False(HasBackups);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(vm.IsConfirming);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Patched(original), File.ReadAllBytes(ExePath));
        var backup = Assert.Single(Directory.GetDirectories(Folder.BackupDirectory));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(backup, Exe)));
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Backup: ", StringComparison.Ordinal));
        Assert.Contains(vm.Log, l => l.Message.Contains("hashes conferidos", StringComparison.Ordinal));
        Assert.Equal(BuildPatchState.Applied, vm.PatchState);
        Assert.False(vm.PatchCommand.CanExecute(null));
        Assert.True(vm.RestoreCommand.CanExecute(null));
        Assert.Empty(StagedFiles());
    }

    [Fact]
    public async Task Cancelar_a_confirmacao_nao_grava_e_apaga_a_copia_de_trabalho()
    {
        var original = File.ReadAllBytes(ExePath);
        var vm = await OpenAsync();
        await vm.PatchCommand.ExecuteAsync(null);

        vm.CancelConfirmationCommand.Execute(null);

        Assert.False(vm.IsConfirming);
        Assert.Empty(vm.ConfirmationFiles);
        Assert.Equal(original, File.ReadAllBytes(ExePath));
        Assert.False(HasBackups);
        Assert.Empty(StagedFiles());
        Assert.Equal(BuildPatchState.Available, vm.PatchState);
        Assert.True(vm.PatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_originais_devolve_o_exe_byte_a_byte_e_a_lista_mostra_o_executavel()
    {
        var original = File.ReadAllBytes(ExePath);
        var vm = await OpenAsync();
        await vm.PatchCommand.ExecuteAsync(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.RestoreCommand.Execute(null);

        var file = Assert.Single(vm.ConfirmationFiles);
        Assert.Equal(Exe, file.Path);
        Assert.Equal("Devolve o executável original", file.Kind);
        Assert.Equal(Patched(original), File.ReadAllBytes(ExePath));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(original, File.ReadAllBytes(ExePath));
        Assert.Equal(BuildPatchState.Available, vm.PatchState);
        Assert.True(vm.PatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exe_recusado_pelo_core_mostra_o_motivo_e_nao_grava_nada()
    {
        var notExe = SyntheticExe();
        notExe[0] = (byte)'X';
        File.WriteAllBytes(ExePath, notExe);

        var vm = await OpenAsync();

        Assert.True(vm.HasPatch);
        Assert.Equal(BuildPatchState.Refused, vm.PatchState);
        Assert.Contains("foi recusado e nada foi gravado no jogo", vm.PatchText);
        Assert.Contains("MZ", vm.PatchText);
        Assert.False(vm.PatchCommand.CanExecute(null));
        Assert.Equal(notExe, File.ReadAllBytes(ExePath));
        Assert.False(HasBackups);
    }

    [Fact]
    public async Task Exe_sem_o_nome_da_fonte_e_recusado_com_o_motivo()
    {
        var other = SyntheticExe();
        Array.Fill(other, (byte)0xCC, NameOffset - 1, 0x20);
        Array.Clear(other, 0x800, 0x20);
        File.WriteAllBytes(ExePath, other);

        var vm = await OpenAsync();

        Assert.Equal(BuildPatchState.Refused, vm.PatchState);
        Assert.Contains("Não achei o nome da fonte", vm.PatchText);
        Assert.False(vm.PatchCommand.CanExecute(null));
        Assert.False(HasBackups);
    }

    [Fact]
    public async Task Patch_ja_aplicado_mostra_o_estado_e_nao_cria_backup()
    {
        var patched = Patched(SyntheticExe());
        File.WriteAllBytes(ExePath, patched);

        var vm = await OpenAsync();

        Assert.True(vm.HasPatch);
        Assert.Equal(BuildPatchState.Applied, vm.PatchState);
        Assert.Contains("já está aplicado", vm.PatchText);
        Assert.False(vm.PatchCommand.CanExecute(null));
        Assert.Equal(patched, File.ReadAllBytes(ExePath));
        Assert.False(HasBackups);
    }

    [Fact]
    public async Task Exe_que_mudou_depois_da_confirmacao_nao_e_gravado()
    {
        var vm = await OpenAsync();
        await vm.PatchCommand.ExecuteAsync(null);
        var changed = SyntheticExe();
        changed[0x900] = 0x42;
        File.WriteAllBytes(ExePath, changed);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(changed, File.ReadAllBytes(ExePath));
        Assert.False(vm.IsConfirming);
        Assert.Empty(StagedFiles());
    }

    [Fact]
    public async Task Recarregar_a_tela_durante_a_confirmacao_mantem_o_que_foi_mostrado()
    {
        var original = File.ReadAllBytes(ExePath);
        var vm = await OpenAsync();
        await vm.PatchCommand.ExecuteAsync(null);

        await vm.ReloadAsync();

        Assert.Equal(BuildConfirmation.Patch, vm.Pending);
        Assert.Equal(BuildPatchState.Available, vm.PatchState);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Patched(original), File.ReadAllBytes(ExePath));
    }

    [Fact]
    public async Task Trocar_de_projeto_durante_a_confirmacao_apaga_a_copia_de_trabalho()
    {
        var vm = await OpenAsync();
        await vm.PatchCommand.ExecuteAsync(null);

        vm.Project = null;

        Assert.False(vm.IsConfirming);
        Assert.Equal(BuildPatchState.None, vm.PatchState);
        Assert.Empty(StagedFiles());
    }

    private sealed class NoStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
