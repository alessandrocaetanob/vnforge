using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using VNForge.Installer.ViewModels;
using VNForge.Patching.Tests;
using VNForge.Patching;

namespace VNForge.Installer.Tests;

/// <summary>As cinco janelas do quadro #540 e as que ele deixou como estados que faltam: o que cada uma mostra, como se avança, o cancelamento e o erro com restauração.</summary>
[Trait("Risco", "R1")]
[Trait("Risco", "R6")]
public sealed class InstallerViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<InstallerViewModel> StartedAsync(IInstallerEngine engine, string current, FakeShell? shell = null, string? folder = null, TimeProvider? time = null, Action<Action>? post = null)
    {
        var vm = new InstallerViewModel(engine, shell ?? new FakeShell(), new InstallerStart(folder, current, null), time, post);
        await vm.StartAsync();
        return vm;
    }

    private static InstallerViewModel Create(IInstallerEngine engine, string current, FakeShell? shell = null, TimeProvider? time = null, Action<Action>? post = null) =>
        new(engine, shell ?? new FakeShell(), new InstallerStart(null, current, null), time, post);

    private static void AssertGameUntouched(SyntheticGame game) => SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);

    // ---- 1 · Pasta do jogo

    // ---- 2 · Instalando

    [Fact]
    public async Task Cancelar_antes_de_mexer_no_jogo_para_com_o_jogo_como_estava()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var engine = new HookedEngine(new PatchEngine(package))
        {
            After = p =>
            {
                if (p is { Phase: PatchPhase.Backup, Done: 1 })
                {
                    reached.Set();
                    release.Wait(TimeSpan.FromMinutes(3));
                }
            },
        };
        var vm = await StartedAsync(engine, game.Game);

        var install = vm.InstallCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), Ct);
        vm.CancelInstallCommand.Execute(null);
        release.Set();
        await install;

        Assert.Equal(InstallerScreen.Stopped, vm.Screen);
        Assert.Equal("Instalação cancelada", vm.Title);
        Assert.Equal(InstallerCardKind.Ok, vm.Card!.Kind);
        Assert.Equal("Os arquivos do jogo estão como antes", vm.Card.Title);
        Assert.Equal(InstallerApp.Cancelled, vm.ExitCode);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ShowRetry);
        Assert.Null(PatchInstaller.InstalledVersion(game.Game));
        AssertGameUntouched(game);
    }

    // ---- 3 · Concluído

    // ---- 4 · Versão errada

    // ---- 5 · Já instalada

    private static async Task<InstallerViewModel> InstalledAsync(SyntheticGame game, PatchPackage package, FakeShell? shell = null)
    {
        PatchInstaller.Install(game.Game, package, null, Ct);
        return await StartedAsync(new PatchEngine(package), game.Game, shell);
    }

    [Fact]
    public async Task Janela_5_voltar_ao_original_devolve_o_jogo_e_mostra_a_janela_final()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await InstalledAsync(game, package);

        await vm.RevertCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Reverted, vm.Screen);
        Assert.Equal("Jogo original de volta", vm.Title);
        Assert.Equal("Pronto. O jogo está como antes da tradução.", vm.Card!.Title);
        Assert.Equal("3 arquivos voltaram ao original, byte a byte.", vm.Card.Detail);
        Assert.Equal(InstallerApp.Ok, vm.ExitCode);
        AssertGameUntouched(game);
    }

    [Fact]
    public async Task Janela_5_com_arquivo_mexido_depois_da_instalacao_avisa_que_ele_fica_como_esta()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        File.WriteAllBytes(Path.Combine(game.Game, "Game/Game.exe"), [1, 2, 3]);

        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);

        Assert.Equal(InstallerScreen.AlreadyInstalled, vm.Screen);
        Assert.Equal("A tradução 1.2.0 já está instalada", vm.Title);
        Assert.Equal(InstallerCardKind.Ok, vm.Card!.Kind);
        Assert.Equal("2 arquivos para devolver", vm.Card.Title);
        Assert.Contains("Game.exe fica como está: mudou depois da instalação.", vm.Card.Detail);
        Assert.True(vm.RevertCommand.CanExecute(null));

        await vm.RevertCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Reverted, vm.Screen);
        Assert.Contains("Deixei Game.exe como está: mudou depois da instalação.", vm.Card!.Detail);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(game.Game, "Game/Game.exe")));
        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Original, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.False(File.Exists(Path.Combine(game.Game, SyntheticGame.CreatedFile)));
    }

    [Fact]
    public async Task Janela_5_sem_a_copia_de_um_original_nao_deixa_voltar_e_diz_qual()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        File.Delete(Path.Combine(game.Game, PatchInstaller.BackupFolderName, "Game/script.ypf"));

        var vm = await StartedAsync(new PatchEngine(package), game.Game);

        Assert.Equal(InstallerScreen.AlreadyInstalled, vm.Screen);
        Assert.Equal(InstallerCardKind.Error, vm.Card!.Kind);
        Assert.Equal("A cópia do original de script.ypf não confere", vm.Card.Title);
        Assert.False(vm.RevertCommand.CanExecute(null));
    }

    // ---- Erro com restauração

    [Fact]
    public async Task Erro_no_meio_da_instalacao_devolve_o_jogo_e_a_janela_diz_o_que_aconteceu()
    {
        using var game = new SyntheticGame();
        var bytes = game.BuildPackage().ToArray();
        using (var intact = PatchPackage.Open(new MemoryStream(bytes)))
        {
            var entry = intact.Manifest.Files.Single(f => f.Path == "Game/script.ypf");
            bytes[intact.PayloadStart + entry.Offset + entry.Length / 2] ^= 0xFF;
        }

        using var package = PatchPackage.Open(new MemoryStream(bytes));
        var vm = await StartedAsync(new PatchEngine(package), game.Game);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Stopped, vm.Screen);
        Assert.Equal("A instalação não terminou", vm.Title);
        Assert.Equal(InstallerCardKind.Error, vm.Card!.Kind);
        Assert.Equal("Não foi possível instalar a tradução", vm.Card.Title);
        Assert.Contains("voltaram ao estado de antes desta instalação", vm.Card.Detail);
        Assert.Equal(InstallerApp.Failed, vm.ExitCode);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ShowRetry);
        Assert.True(vm.ShowClose);
        Assert.Null(PatchInstaller.InstalledVersion(game.Game));
        AssertGameUntouched(game);
        Assert.DoesNotContain(Directory.EnumerateFiles(game.Game, "*", SearchOption.AllDirectories), f => f.EndsWith(".vnf-tmp", StringComparison.Ordinal));

        await vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal(game.Game, vm.FolderPath);
    }

    // ---- Instalador vazio e dados da coluna

    // ---- Dublês

    private sealed class FakeShell : IInstallerShell
    {
        public string? Game { get; private set; }

        public string? ReadMe { get; private set; }

        public bool OpenGameFails { get; init; }

        public string? FindGame(string folder, PatchManifest manifest) => GameExecutable.Find(folder, manifest);

        public void OpenGame(string executablePath)
        {
            if (OpenGameFails)
            {
                throw new InvalidOperationException("sem programa associado.");
            }

            Game = executablePath;
        }

        public void OpenReadMe(string text) => ReadMe = text;
    }

    /// <summary>O engine de verdade com ganchos no andamento: <see cref="Before"/> roda antes de a janela ver o passo, <see cref="After"/> depois (e pode bloquear a instalação ali).</summary>
    private sealed class HookedEngine(IInstallerEngine inner) : IInstallerEngine
    {
        public Action<PatchProgress>? Before { get; init; }

        public Action<PatchProgress>? After { get; init; }

        public Exception? FailInstallWith { get; init; }

        public Exception? FailUninstallWith { get; init; }

        public PatchManifest Manifest => inner.Manifest;

        public PatchCheck Check(string folder) => inner.Check(folder);

        public PatchUninstallPlan PlanUninstall(string folder) => inner.PlanUninstall(folder);

        public PatchInstallResult Install(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken) =>
            FailInstallWith is { } fail ? throw fail : inner.Install(folder, new Hook(progress, Before, After), cancellationToken);

        public PatchUninstallResult Uninstall(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken) =>
            FailUninstallWith is { } fail ? throw fail : inner.Uninstall(folder, new Hook(progress, Before, After), cancellationToken);

        private sealed class Hook(IProgress<PatchProgress> target, Action<PatchProgress>? before, Action<PatchProgress>? after) : IProgress<PatchProgress>
        {
            public void Report(PatchProgress value)
            {
                before?.Invoke(value);
                target.Report(value);
                after?.Invoke(value);
            }
        }
    }
}
