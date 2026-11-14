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
[Trait("Risco", "R1")]
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
