using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Injection;
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
    private FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

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
        Assert.Contains("Falta ou sobra um marcador (2)", vm.GameBannerMessage, StringComparison.Ordinal);
        Assert.Contains("Conferir a tradução", vm.GameBannerMessage, StringComparison.Ordinal);
        Assert.False(vm.TestCommand.CanExecute(null));
        Assert.True(vm.TestAccentsCommand.CanExecute(null));
        Assert.True(vm.ReturnToOriginalCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sem_teste_os_cinco_passos_ficam_cinza_com_a_frase_de_projeto_sem_teste()
    {
        var vm = await OpenAsync();

        Assert.Equal(5, vm.LastTestSteps.Count);
        Assert.All(vm.LastTestSteps, r => Assert.True(r.IsPending));
        Assert.All(vm.LastTestSteps, r => Assert.Equal(string.Empty, r.TimeText));
        Assert.Equal([false, false, false, false, true], vm.LastTestSteps.Select(r => r.IsLast));
        Assert.Equal("Ainda não houve teste neste projeto", vm.TestStepsCaption);
    }

    [Fact]
    public async Task Com_erros_so_o_passo_Conferir_fica_travado_e_diz_quantos_erros()
    {
        qaRule.Enabled = true;

        var vm = await OpenAsync();

        Assert.True(vm.LastTestSteps[0].IsBlocked);
        Assert.Equal("2 erros travam", vm.LastTestSteps[0].TimeText);
        Assert.All(vm.LastTestSteps.Skip(1), r => Assert.True(r.IsPending));
        Assert.Equal("Corrija os erros na Conferência para testar de novo.", vm.TestStepsCaption);
    }

    [Fact]
    public void Durante_o_teste_os_passos_anteriores_ficam_prontos_o_atual_em_andamento_e_os_outros_cinza()
    {
        var rows = BuildViewModel.RunningRows(GameTestStep.Backup);

        Assert.Equal(
            [TestStepState.Done, TestStepState.Done, TestStepState.Running, TestStepState.Pending, TestStepState.Pending],
            rows.Select(r => r.State));
        Assert.Equal(["Conferir a tradução", "Preparar os arquivos", "Guardar a cópia de segurança", "Copiar para o jogo", "Abrir o jogo"], rows.Select(r => r.Label));
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
        Assert.Equal(vm.LastTestTitle, vm.TestStepsCaption);
        Assert.All(vm.LastTestSteps, r => Assert.True(r.IsDone));
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

        Assert.Contains("Há erros que travam o teste", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(GameFile, TestContext.Current.CancellationToken));
        Assert.Null(starter.Executable);
    }

    private static void AssertPlain(string? shown)
    {
        Assert.False(string.IsNullOrEmpty(shown));
        Assert.DoesNotContain(@":\", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("/", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("vnf ", shown, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveInGame(DateTimeOffset at, string? backup) =>
        GameStateFile.Save(ProjectFolder.Open(root), GameStateFile.Empty with { InGame = new GameInGame(GameTestKind.Translation, at, backup, null, null) });

    [Fact]
    public async Task Falha_que_o_Core_descreve_com_caminho_aparece_na_barra_sem_caminho_e_vai_inteira_para_o_registro()
    {
        starter.Failure = new VNForgeException(@"Não foi possível abrir o jogo: C:\Jogos\MinhaVN.exe. Rode vnf run depois.");
        var vm = await OpenAsync();

        await vm.TestCommand.ExecuteAsync(null);

        AssertPlain(vm.ErrorMessage);
        Assert.Contains("recebeu a tradução, mas não abriu", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(vm.Log, l => l.Message.Contains(@"C:\Jogos\MinhaVN.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Erro_qualquer_do_teste_mostra_uma_frase_fixa_e_o_resto_vai_para_o_registro()
    {
        var vm = await OpenAsync();
        Directory.Delete(Path.Combine(root, ".cache", "game"), recursive: true);

        await vm.TestCommand.ExecuteAsync(null);

        AssertPlain(vm.ErrorMessage);
        Assert.Contains("Detalhes técnicos", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(vm.Log, l => l.Message.StartsWith("O teste parou: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Com_outra_operacao_no_jogo_a_barra_diz_que_esta_ocupado_sem_caminho()
    {
        var vm = await OpenAsync();
        var before = File.ReadAllBytes(GameFile);
        using var held = ProjectLock.Acquire(ProjectFolder.Open(root));

        await vm.TestCommand.ExecuteAsync(null);
        await vm.ReturnToOriginalCommand.ExecuteAsync(null);

        AssertPlain(vm.ErrorMessage);
        Assert.Contains("Outra operação do VNForge", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Estado_diz_que_o_jogo_tem_a_traducao_mas_o_backup_sumiu_a_faixa_avisa_e_nao_diz_como_veio()
    {
        SaveInGame(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), "2026-10-22_2316");
        Directory.Delete(Path.Combine(root, "backup"), recursive: true);

        var vm = await OpenAsync();

        Assert.Equal(GameState.Warning, vm.GameBannerState);
        Assert.Equal("O jogo está com a tradução, mas a cópia de segurança não foi encontrada", vm.GameBannerTitle);
        Assert.Contains("não está mais no projeto", vm.GameBannerMessage, StringComparison.Ordinal);
        Assert.Contains("reinstale o jogo", vm.GameBannerMessage, StringComparison.Ordinal);
        Assert.Contains("sem cópia de segurança", vm.GameStatusText, StringComparison.Ordinal);
        Assert.False(vm.ReturnToOriginalCommand.CanExecute(null));
        Assert.Contains("Reinstale o jogo", vm.ReturnToOriginalHint, StringComparison.Ordinal);
        AssertPlain(vm.GameBannerMessage.Replace("/", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Estado_com_manifesto_ilegivel_tambem_avisa_com_o_motivo()
    {
        SaveInGame(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), "2026-10-22_2316");
        var manifest = Directory.GetFiles(Path.Combine(root, "backup"), BackupManifest.FileName, SearchOption.AllDirectories);
        foreach (var file in manifest)
        {
            File.WriteAllText(file, "{ não é json");
        }

        var vm = await OpenAsync();

        Assert.Equal(GameState.Warning, vm.GameBannerState);
        Assert.Contains("não consegue ler", vm.GameBannerMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backup_restaurado_por_fora_nao_gera_aviso_e_a_faixa_diz_como_veio()
    {
        SaveInGame(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), "2026-10-22_2316");
        new GameInjector().Restore(GameInjector.PlanRestore(ProjectFolder.Open(root)));

        var vm = await OpenAsync();

        Assert.Equal(GameState.Original, vm.GameBannerState);
    }

    [Fact]
    public async Task Estado_mais_velho_que_o_backup_mais_novo_perde_para_o_backup()
    {
        SaveInGame(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), "2026-10-01_0900");

        var vm = await OpenAsync();

        Assert.Equal(GameState.Translated, vm.GameBannerState);
        Assert.Equal($"O jogo está com a tradução de {DateText(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero))}", vm.GameBannerTitle);
        Assert.Equal($"Jogo com a tradução de {DateText(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero))}", vm.GameStatusText);
    }

    [Fact]
    public async Task Injecao_que_falhou_e_foi_desfeita_deixa_a_faixa_em_como_veio()
    {
        Directory.Delete(Path.Combine(root, "backup"), recursive: true);
        File.WriteAllBytes(Path.Combine(root, "build", DemoBuild.Archive), DemoBuild.Payload(4_200_000, 9));
        var plan = GameInjector.Plan(ProjectFolder.Open(root));

        Assert.Throws<InjectionException>(() =>
            GameStateFile.Inject(ProjectFolder.Open(root), new GameInjector(clock, new FailsFirstCopy()), plan, GameTestKind.Translation, null, clock));
        var vm = await OpenAsync();

        Assert.Equal(GameState.Original, vm.GameBannerState);
        Assert.Equal("O jogo está como veio", vm.GameBannerTitle);
        Assert.Null(GameStateFile.Load(ProjectFolder.Open(root)).InGame);
    }

    [Theory]
    [InlineData(2020)]
    [InlineData(2026)]
    [InlineData(2030)]
    public async Task Injetar_dos_detalhes_tecnicos_atualiza_o_estado_do_jogo_em_qualquer_data(int year)
    {
        clock = new FakeTimeProvider(new DateTimeOffset(year, 6, 15, 12, 0, 0, TimeSpan.Zero));
        Directory.Delete(Path.Combine(root, "backup"), recursive: true);
        File.WriteAllBytes(Path.Combine(root, "build", DemoBuild.Archive), DemoBuild.Payload(4_200_000, 9));
        var vm = await OpenAsync();

        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        var inGame = GameStateFile.Load(ProjectFolder.Open(root)).InGame;
        Assert.NotNull(inGame?.BackupDirectory);
        Assert.Equal(GameState.Translated, vm.GameBannerState);
        Assert.Equal($"O jogo está com a tradução de {DateText(clock.GetUtcNow())}", vm.GameBannerTitle);
    }

    private sealed class FailsFirstCopy : IInjectionFileSystem
    {
        private int copies;

        public void CopyFile(string source, string destination)
        {
            if (++copies == 2)
            {
                throw new IOException("disco cheio");
            }

            File.Copy(source, destination, overwrite: true);
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class Starter : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public VNForgeException? Failure { get; set; }

        public void Start(string executablePath, string workingDirectory)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Executable = executablePath;
        }
    }
}
