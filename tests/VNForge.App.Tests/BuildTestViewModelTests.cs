using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Demo;
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
    private readonly DemoBuildEngine engine = new();
    private readonly GateQaRule gate = new();
    private readonly BackgroundTaskService tasks = new();
    private FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    public BuildTestViewModelTests()
    {
        root = DemoBuild.Create(temp.SubFolder("demo"));
    }

    public void Dispose() => temp.Dispose();

    private string GameFile => Path.Combine(root, "..", "jogo", DemoBuild.Archive);

    private async Task<BuildViewModel> OpenAsync(IInjectionFileSystem? injectionFiles = null, BackgroundTaskService? service = null)
    {
        var vm = new BuildViewModel(service ?? tasks, [engine], starter, new QaEngine([qaRule, gate]), clock, injectionFiles)
        {
            Project = DemoProject.Summary(root),
        };
        await vm.ReloadAsync();
        return vm;
    }

    private static string DateText(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);

    private static TestStepState[] States(BuildViewModel vm) => [.. vm.LastTestSteps.Select(r => r.State)];

    private async Task<Task> StartHeldAsync(BuildViewModel vm, bool accents = false)
    {
        starter.Hold = new ManualResetEventSlim();
        var running = accents ? vm.TestAccentsCommand.ExecuteAsync(null) : vm.TestCommand.ExecuteAsync(null);
        Assert.True(await Task.Run(() => starter.Entered.Wait(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken), "O teste não chegou a abrir o jogo.");
        return running;
    }

    [Fact]
    public async Task Voltar_a_pagina_durante_o_teste_nao_reescreve_os_passos_com_o_ultimo_teste()
    {
        var vm = await OpenAsync();
        var running = await StartHeldAsync(vm);

        await vm.ReloadAsync();

        Assert.Equal(TestStepState.Running, vm.LastTestSteps[4].State);
        Assert.Equal("Testando agora · Abrir o jogo", vm.TestStepsCaption);

        starter.Hold!.Set();
        await running;
    }

    [Fact]
    public async Task Falha_no_meio_do_teste_deixa_o_passo_que_falhou_vermelho_os_anteriores_prontos_e_a_legenda_diz_que_falhou()
    {
        starter.Failure = new GameOpenException("não abriu", new IOException("sem acesso"));
        var vm = await OpenAsync();

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Equal(
            [TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Failed],
            States(vm));
        Assert.Equal("O jogo não abriu", vm.LastTestSteps[4].TimeText);
        Assert.Equal("O último teste falhou.", vm.TestStepsCaption);
        Assert.NotNull(vm.ErrorMessage);

        starter.Failure = null;
        await vm.TestCommand.ExecuteAsync(null);

        Assert.All(vm.LastTestSteps, r => Assert.True(r.IsDone));
    }

    /// <summary>
    /// A falha vem de uma exceção sem ramo próprio na tela, num trabalho rápido: o aviso do passo chega pelo Dispatcher depois do fim do trabalho, então o passo
    /// que falhou tem que vir da exceção, não do último aviso recebido. O Dispatcher aqui só roda o que o teste solta, depois do fim do teste.
    /// </summary>
    [Theory]
    [InlineData(GameTestStep.Check)]
    [InlineData(GameTestStep.Build)]
    [InlineData(GameTestStep.Backup)]
    [InlineData(GameTestStep.Inject)]
    [InlineData(GameTestStep.Open)]
    public async Task Falha_sem_ramo_proprio_marca_o_passo_em_que_aconteceu_e_nao_o_anterior(GameTestStep failing)
    {
        var failure = new InvalidOperationException("quebrou");
        var late = new ConcurrentQueue<Action>();
        var vm = await OpenAsync(
            failing is GameTestStep.Backup or GameTestStep.Inject ? new FailsOnCopy(failing == GameTestStep.Backup ? 1 : 2, failure) : null,
            new BackgroundTaskService(late.Enqueue));
        switch (failing)
        {
            case GameTestStep.Check:
                gate.Arm(failure);
                break;
            case GameTestStep.Build:
                engine.RepackFailure = failure;
                break;
            case GameTestStep.Open:
                starter.Failure = new GameOpenException("não abriu", failure);
                break;
        }

        await vm.TestCommand.ExecuteAsync(null);
        Assert.NotEmpty(late);

        var index = (int)failing;
        Assert.Equal(
            [.. Enum.GetValues<GameTestStep>().Select(s => s < failing ? TestStepState.Done : s == failing ? TestStepState.Failed : TestStepState.Pending)],
            States(vm));
        Assert.Equal(FailureTexts[index], vm.LastTestSteps[index].TimeText);
        Assert.Equal("O último teste falhou.", vm.TestStepsCaption);
        Assert.NotNull(vm.ErrorMessage);
    }

    private static readonly string[] FailureTexts = ["Não deu para conferir", "Não deu para preparar", "Não deu para guardar", "Não deu para copiar", "O jogo não abriu"];

    [Fact]
    public async Task Cancelar_com_os_avisos_dos_passos_atrasados_marca_o_passo_em_que_o_cancelamento_foi_percebido()
    {
        var late = new ConcurrentQueue<Action>();
        var service = new BackgroundTaskService(late.Enqueue);
        var vm = await OpenAsync(service: service);
        gate.Arm();
        var running = vm.TestCommand.ExecuteAsync(null);
        Assert.True(await Task.Run(() => gate.Entered.Wait(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken), "O teste não chegou à Conferência.");

        service.Tasks.Last().CancelCommand.Execute(null);
        gate.Release.Set();
        await running;

        Assert.Equal(
            [TestStepState.Done, TestStepState.Failed, TestStepState.Pending, TestStepState.Pending, TestStepState.Pending],
            States(vm));
        Assert.Equal("Cancelado", vm.LastTestSteps[1].TimeText);
    }

    [Fact]
    public async Task Testar_so_os_acentos_mostra_o_proprio_resultado_mesmo_com_erros_na_Conferencia()
    {
        var vm = await OpenAsync();
        await vm.TestAccentsCommand.ExecuteAsync(null);
        qaRule.Enabled = true;

        await vm.ReloadAsync();

        Assert.All(vm.LastTestSteps, r => Assert.True(r.IsDone));
        Assert.Contains("só os acentos", vm.TestStepsCaption, StringComparison.Ordinal);
        Assert.Contains("corrija os erros", vm.TestStepsCaption, StringComparison.Ordinal);
        Assert.Equal(GameState.Blocked, vm.GameBannerState);
        Assert.False(vm.TestCommand.CanExecute(null));
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
    public async Task Reconstruir_so_scripts_alterados_chega_ao_teste_no_jogo()
    {
        var vm = await OpenAsync();
        Assert.True(vm.OnlyChanged);
        var buildFiles = () => Directory.EnumerateFiles(Path.Combine(root, "build"), "*", SearchOption.AllDirectories).ToList();
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await vm.TestCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.NotEmpty(buildFiles());
        foreach (var file in buildFiles())
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        await vm.TestCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.All(buildFiles(), f => Assert.Equal(old, File.GetLastWriteTimeUtc(f)));

        vm.OnlyChanged = false;
        await vm.TestCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(buildFiles(), f => File.GetLastWriteTimeUtc(f) != old);
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

    /// <summary>Falha na cópia de número <c>failingCopy</c>: a 1 é a do original para o backup, a 2 é a gravação no jogo.</summary>
    private sealed class FailsOnCopy(int failingCopy, Exception failure) : IInjectionFileSystem
    {
        private int copies;

        public void CopyFile(string source, string destination)
        {
            if (++copies == failingCopy)
            {
                throw failure;
            }

            File.Copy(source, destination, overwrite: true);
        }

        public void DeleteFile(string path) => File.Delete(path);
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

        /// <summary>Com ele, <see cref="Start"/> espera aqui: o teste fica parado no passo "Abrir o jogo".</summary>
        public ManualResetEventSlim? Hold { get; set; }

        public ManualResetEventSlim Entered { get; } = new();

        public void Start(string executablePath, string workingDirectory)
        {
            Entered.Set();
            Hold?.Wait(TimeSpan.FromSeconds(30));
            if (Failure is not null)
            {
                throw Failure;
            }

            Executable = executablePath;
        }
    }

    /// <summary>Segura a Conferência do teste uma vez, depois de armada, para o teste poder ser cancelado ali; as leituras da tela passam direto.</summary>
    private sealed class GateQaRule : IQaRule
    {
        private int armed;
        private Exception? failure;

        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public string Id => "gate";

        public string Description => "Segura a Conferência para o teste.";

        public void Arm() => Volatile.Write(ref armed, 1);

        /// <summary>Faz a próxima Conferência do teste falhar com <paramref name="exception"/>, sem segurar.</summary>
        public void Arm(Exception exception)
        {
            failure = exception;
            Arm();
        }

        public IEnumerable<QaIssue> Check(QaContext context)
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                if (failure is not null)
                {
                    throw failure;
                }

                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }

            return [];
        }
    }
}
