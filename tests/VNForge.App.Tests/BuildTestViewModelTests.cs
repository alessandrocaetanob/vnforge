using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.Tests;

/// <summary>"Testar no jogo" no ViewModel: o botão único, a faixa de estado, "Voltar ao jogo original" e os cinco passos do último teste.</summary>
public sealed class BuildTestViewModelTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string root;
    private readonly DemoQaRule qaRule = new();
    private readonly Starter starter = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    public BuildTestViewModelTests()
    {
        root = DemoBuild.Create(temp.SubFolder("demo"));
    }

    public void Dispose() => temp.Dispose();

    private string GameFile => Path.Combine(root, "..", "jogo", DemoBuild.Archive);

    private async Task<BuildViewModel> OpenAsync()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [new DemoBuildEngine()], starter, new QaEngine([qaRule]), clock)
        {
            Project = DemoProject.Summary(root),
        };
        await vm.ReloadAsync();
        return vm;
    }

    private static string DateText(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Jogo_injetado_antes_do_arquivo_de_estado_aparece_como_traduzido_pela_data_do_backup()
    {
        var vm = await OpenAsync();

        Assert.Equal(GameState.Translated, vm.GameBannerState);
        Assert.Equal($"O jogo está com a tradução de {DateText(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero))}", vm.GameBannerTitle);
        Assert.Equal("A tradução continua no projeto, mesmo se você voltar ao original.", vm.GameBannerMessage);
        Assert.StartsWith("Jogo com a tradução de ", vm.GameStatusText, StringComparison.Ordinal);
        Assert.True(vm.ReturnToOriginalCommand.CanExecute(null));
        Assert.False(vm.HasLastTest);
    }

    [Fact]
    public async Task Sem_backup_o_jogo_esta_como_veio_e_nao_ha_o_que_devolver()
    {
        Directory.Delete(Path.Combine(root, "backup"), recursive: true);

        var vm = await OpenAsync();

        Assert.Equal(GameState.Original, vm.GameBannerState);
        Assert.Equal("O jogo está como veio", vm.GameBannerTitle);
        Assert.Equal("Nenhuma tradução foi copiada para o jogo.", vm.GameBannerMessage);
        Assert.Equal("Jogo original", vm.GameStatusText);
        Assert.False(vm.ReturnToOriginalCommand.CanExecute(null));
    }

    [Fact]
    public async Task Erros_da_Conferencia_travam_so_o_teste_completo_e_a_faixa_diz_quais()
    {
        qaRule.Enabled = true;

        var vm = await OpenAsync();

        Assert.Equal(GameState.Blocked, vm.GameBannerState);
        Assert.Equal("2 erros travam o teste no jogo", vm.GameBannerTitle);
        Assert.Contains("Marcadores diferentes do original (2)", vm.GameBannerMessage, StringComparison.Ordinal);
        Assert.Contains("Conferir a tradução", vm.GameBannerMessage, StringComparison.Ordinal);
        Assert.False(vm.TestCommand.CanExecute(null));
        Assert.True(vm.TestAccentsCommand.CanExecute(null));
        Assert.True(vm.ReturnToOriginalCommand.CanExecute(null));
    }

    [Fact]
    public async Task Testar_no_jogo_confere_injeta_com_backup_abre_o_jogo_e_guarda_os_cinco_passos()
    {
        var vm = await OpenAsync();
        var before = Directory.GetDirectories(Path.Combine(root, "backup")).Length;
        BuildNotice? notice = null;
        vm.NoticeRaised += (_, raised) => notice = raised;

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(before + 1, Directory.GetDirectories(Path.Combine(root, "backup")).Length);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "..", "jogo", "MinhaVN.exe")), Path.GetFullPath(starter.Executable!));
        Assert.Equal(GameState.Translated, vm.GameBannerState);
        Assert.Equal($"O jogo está com a tradução de {DateText(clock.GetUtcNow())}", vm.GameBannerTitle);
        Assert.Equal(
            ["Conferir a tradução", "Preparar os arquivos", "Guardar a cópia de segurança", "Copiar para o jogo", "Abrir o jogo"],
            vm.LastTestSteps.Select(s => s.Label));
        Assert.StartsWith("Último teste · ", vm.LastTestTitle, StringComparison.Ordinal);
        Assert.Equal(BuildNoticeKind.Tested, notice?.Kind);
        Assert.Equal(GameTestKind.Translation, GameStateFile.Load(ProjectFolder.Open(root)).InGame?.Kind);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Voltar_ao_jogo_original_devolve_os_arquivos_byte_a_byte_sem_pedir_confirmacao()
    {
        var vm = await OpenAsync();
        await vm.TestCommand.ExecuteAsync(null);
        Assert.NotEqual(DemoBuild.Payload(DemoBuild.OriginalBytes, 1), await File.ReadAllBytesAsync(GameFile, TestContext.Current.CancellationToken));

        await vm.ReturnToOriginalCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsConfirming);
        Assert.Equal(DemoBuild.Payload(DemoBuild.OriginalBytes, 1), await File.ReadAllBytesAsync(GameFile, TestContext.Current.CancellationToken));
        Assert.Equal(GameState.Original, vm.GameBannerState);
        Assert.Equal("Jogo original", vm.GameStatusText);
        Assert.False(vm.ReturnToOriginalCommand.CanExecute(null));
        Assert.True(vm.HasLastTest);
        Assert.Null(GameStateFile.Load(ProjectFolder.Open(root)).InGame);
    }

    [Fact]
    public async Task Testar_so_os_acentos_injeta_a_frase_e_a_faixa_diz_que_e_o_teste_de_acentos()
    {
        var vm = await OpenAsync();

        await vm.TestAccentsCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(GameTestKind.AccentTest, GameStateFile.Load(ProjectFolder.Open(root)).InGame?.Kind);
        Assert.Contains("só os acentos", vm.LastTestTitle, StringComparison.Ordinal);
        Assert.Contains("teste de acentos", vm.GameBannerMessage + vm.GameStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task So_a_cena_aberta_usa_a_cena_do_editor_e_desliga_sem_cena()
    {
        var vm = await OpenAsync();

        Assert.False(vm.HasOpenScene);
        Assert.Contains("Abra uma cena", vm.OpenSceneDescription, StringComparison.Ordinal);

        vm.SetOpenScene("yst00013", "Capítulo 1 — Manhã de chuva");
        vm.OnlyOpenScene = true;
        await vm.TestCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("yst00013", GameStateFile.Load(ProjectFolder.Open(root)).InGame?.ScriptKey);
        Assert.Contains("Manhã de chuva", vm.OpenSceneDescription, StringComparison.Ordinal);

        vm.SetOpenScene(null, null);

        Assert.False(vm.OnlyOpenScene);
        Assert.False(vm.HasOpenScene);
    }

    [Fact]
    public async Task Jogo_sem_o_executavel_nao_recebe_nada_e_a_mensagem_nao_mostra_o_caminho()
    {
        var vm = await OpenAsync();
        File.Delete(Path.Combine(root, "..", "jogo", "MinhaVN.exe"));
        var before = File.ReadAllBytes(GameFile);

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Equal(before, await File.ReadAllBytesAsync(GameFile, TestContext.Current.CancellationToken));
        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(vm.Log, l => l.Message.Contains("MinhaVN.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Erro_de_Conferencia_no_meio_recusa_em_linguagem_comum_e_nao_grava()
    {
        var vm = await OpenAsync();
        var before = File.ReadAllBytes(GameFile);
        qaRule.Enabled = true;

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Contains("A Conferência travou o teste", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(GameFile, TestContext.Current.CancellationToken));
        Assert.Null(starter.Executable);
    }

    private sealed class Starter : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public void Start(string executablePath, string workingDirectory) => Executable = executablePath;
    }
}
