using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using VNForge.Installer.ViewModels;
using VNForge.Patching.Tests;
using VNForge.Patching;

namespace VNForge.Installer.Tests;

/// <summary>As cinco janelas do quadro #540 e as que ele deixou como estados que faltam: o que cada uma mostra, como se avança, o cancelamento e o erro com restauração.</summary>
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

    [Fact]
    public async Task Janela_1_com_o_jogo_na_pasta_atual_mostra_a_pasta_o_cartao_e_deixa_instalar()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await StartedAsync(new PatchEngine(package), game.Game);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal("Instalar a tradução", vm.Title);
        Assert.Equal("Escolha a pasta onde o jogo está instalado. O instalador procurou e achou esta.", vm.Intro);
        Assert.Equal(game.Game, vm.FolderPath);
        Assert.Equal(InstallerCardKind.Ok, vm.Card!.Kind);
        Assert.Equal("Versão do jogo conferida", vm.Card.Title);
        Assert.Matches(@"^Game\.exe confere com a versão esperada \(SHA-256 [0-9a-f]{4}…[0-9a-f]{4}\)\.$", vm.Card.Detail);
        Assert.Equal("Antes de mexer no jogo, o instalador guarda uma cópia dos arquivos originais.", vm.Note);
        Assert.True(vm.InstallCommand.CanExecute(null));
        Assert.True(vm.BrowseCommand.CanExecute(null));
        Assert.Equal(InstallerApp.Cancelled, vm.ExitCode);
    }

    [Fact]
    public async Task Janela_1_sem_jogo_por_perto_pede_a_pasta_e_nao_deixa_instalar()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await StartedAsync(new PatchEngine(package), game.Root);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal("Escolha a pasta onde o jogo está instalado. O instalador procurou e não achou.", vm.Intro);
        Assert.Equal(string.Empty, vm.FolderPath);
        Assert.Null(vm.Card);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Janela_1_com_a_pasta_dada_na_linha_de_comando_nao_diz_que_procurou()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal(game.Game, vm.FolderPath);
        Assert.Equal("Escolha a pasta onde o jogo está instalado.", vm.Intro);
        Assert.Equal(InstallerCardKind.Ok, vm.Card!.Kind);
    }

    [Fact]
    public async Task Janela_1_com_uma_pasta_que_nao_existe_mostra_o_erro_no_cartao()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: Path.Combine(game.Root, "nao-existe"));

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal(InstallerCardKind.Error, vm.Card!.Kind);
        Assert.Equal("Não consegui conferir esta pasta", vm.Card.Title);
        Assert.Contains("não existe", vm.Card.Detail);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Procurar_confere_a_pasta_escolhida_e_cancelar_o_seletor_nao_muda_nada()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await StartedAsync(new PatchEngine(package), game.Root);
        vm.PickFolder = () => Task.FromResult<string?>(null);

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.FolderPath);

        vm.PickFolder = () => Task.FromResult<string?>(game.Game);
        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal(game.Game, vm.FolderPath);
        Assert.Equal("Escolha a pasta onde o jogo está instalado.", vm.Intro);
        Assert.True(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelar_na_janela_1_fecha_com_o_codigo_4_e_nao_altera_o_jogo()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await StartedAsync(new PatchEngine(package), game.Game);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(InstallerApp.Cancelled, vm.ExitCode);
        AssertGameUntouched(game);
        Assert.False(Directory.Exists(Path.Combine(game.Game, PatchInstaller.BackupFolderName)));
    }

    // ---- 2 · Instalando

    [Fact]
    public async Task Janela_2_mostra_as_quatro_etapas_na_ordem_e_avanca_com_tempo_tamanho_e_percentual()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var time = new FakeTimeProvider();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var engine = new HookedEngine(new PatchEngine(package))
        {
            Before = p =>
            {
                if (p is { Phase: PatchPhase.Backup, Done: 0 })
                {
                    time.Advance(TimeSpan.FromMilliseconds(200));
                }
            },
            After = p =>
            {
                if (p is { Phase: PatchPhase.Apply, Done: 2 })
                {
                    reached.Set();
                    release.Wait(TimeSpan.FromMinutes(3));
                }
            },
        };
        var vm = await StartedAsync(engine, game.Game, time: time);

        var install = vm.InstallCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), Ct);

        Assert.Equal(InstallerScreen.Installing, vm.Screen);
        Assert.Equal("Instalando", vm.Title);
        Assert.StartsWith("Não feche o instalador. Leva ", vm.Intro);
        Assert.Equal(["Conferir a versão do jogo", "Guardar a cópia dos originais", "Aplicar a tradução", "Conferir o resultado"], vm.Steps.Select(s => s.Title));
        Assert.Equal([InstallStepState.Done, InstallStepState.Done, InstallStepState.Running, InstallStepState.Pending], vm.Steps.Select(s => s.State));
        Assert.Equal("0,2 s", vm.Steps[0].Detail);
        Assert.Equal($"2 arquivos · {InstallerText.Size((3 * 1024 * 1024) + 200_000)}", vm.Steps[1].Detail);
        Assert.Matches(@"^\d{1,2}%$", vm.Steps[2].Detail);
        Assert.Equal(string.Empty, vm.Steps[3].Detail);
        Assert.InRange(vm.Progress, 1, 99);
        Assert.Equal($"{(int)Math.Round(vm.Progress)}%", vm.Steps[2].Detail);
        Assert.False(vm.CanCancelInstall);
        Assert.False(vm.CancelInstallCommand.CanExecute(null));
        Assert.True(vm.IsBusy);

        release.Set();
        await install;

        Assert.Equal(InstallerScreen.Done, vm.Screen);
        Assert.All(vm.Steps, s => Assert.Equal(InstallStepState.Done, s.State));
        Assert.Equal(100, vm.Progress);
    }

    [Fact]
    public async Task Nas_primeiras_etapas_o_botao_cancelar_esta_ligado()
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

        Assert.Equal(InstallStepState.Running, vm.Steps[1].State);
        Assert.True(vm.CanCancelInstall);
        Assert.True(vm.CancelInstallCommand.CanExecute(null));

        release.Set();
        await install;
    }

    [Fact]
    public async Task O_andamento_so_chega_a_tela_pela_fila_do_post()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var vm = await StartedAsync(new PatchEngine(package), game.Game, post: queue.Enqueue);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Done, vm.Screen);
        Assert.False(queue.IsEmpty);
        Assert.All(vm.Steps, s => Assert.Equal(InstallStepState.Done, s.State));
        while (queue.TryDequeue(out var action))
        {
            action();
        }
    }

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

    [Fact]
    public async Task Cancelar_depois_de_voltar_a_conferir_deixa_instalar_de_novo()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = true;
        var engine = new HookedEngine(new PatchEngine(package))
        {
            After = p =>
            {
                if (first && p is { Phase: PatchPhase.Backup, Done: 1 })
                {
                    first = false;
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

        await vm.RetryCommand.ExecuteAsync(null);
        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Done, vm.Screen);
        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Build, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
    }

    // ---- 3 · Concluído

    [Fact]
    public async Task Janela_3_diz_quantos_arquivos_mudaram_onde_estao_os_originais_e_abre_o_jogo_e_o_leia_me()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var shell = new FakeShell();
        var vm = await StartedAsync(new PatchEngine(package), game.Game, shell);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Done, vm.Screen);
        Assert.Equal("Tradução instalada", vm.Title);
        Assert.Equal("Pronto. Abra o jogo normalmente.", vm.Card!.Title);
        Assert.Equal($"3 arquivos alterados. Os originais estão guardados em game{Path.DirectorySeparatorChar}_originais.", vm.Card.Detail);
        Assert.Equal("Para voltar ao jogo original, execute este instalador de novo e escolha Voltar ao jogo original.", vm.Note);
        Assert.Equal(InstallerApp.Ok, vm.ExitCode);
        Assert.True(vm.ShowReadMe);
        Assert.True(vm.ShowOpenGame);
        Assert.True(vm.ShowClose);
        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Build, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));

        vm.OpenReadMeCommand.Execute(null);
        Assert.Equal("Leia-me", shell.ReadMe);
        vm.OpenGameCommand.Execute(null);
        Assert.Equal(Path.Combine(game.Game, "Game", "Game.exe"), shell.Game);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Sem_executavel_a_abrir_o_botao_abrir_o_jogo_some()
    {
        using var game = new SyntheticGame();
        using var stream = new MemoryStream();
        PatchBuilder.Build(SyntheticGame.Info(), [.. game.Sources().Where(s => !s.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))], stream, cancellationToken: Ct);
        stream.Position = 0;
        using var package = PatchPackage.Open(stream);
        var vm = await StartedAsync(new PatchEngine(package), game.Game);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Done, vm.Screen);
        Assert.False(vm.ShowOpenGame);
        Assert.False(vm.OpenGameCommand.CanExecute(null));
        Assert.True(vm.ShowClose);
    }

    [Fact]
    public async Task Se_o_sistema_nao_abre_o_jogo_a_janela_fica_e_diz_o_motivo()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await StartedAsync(new PatchEngine(package), game.Game, new FakeShell { OpenGameFails = true });
        await vm.InstallCommand.ExecuteAsync(null);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.OpenGameCommand.Execute(null);

        Assert.Equal(0, closed);
        Assert.StartsWith("Não consegui abrir o jogo: ", vm.Note);
    }

    // ---- 4 · Versão errada

    [Fact]
    public async Task Janela_4_diz_qual_arquivo_e_os_dois_hashes_e_nao_grava_nada()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/script.ypf"), [9, 9, 9]);
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await StartedAsync(new PatchEngine(package), game.Game);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal(string.Empty, vm.FolderPath);

        vm.PickFolder = () => Task.FromResult<string?>(game.Game);
        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.WrongVersion, vm.Screen);
        Assert.Equal("Esta não é a versão esperada", vm.Title);
        Assert.Equal(InstallerCardKind.Error, vm.Card!.Kind);
        Assert.Equal("O script.ypf desta pasta é diferente", vm.Card.Title);
        Assert.Matches(@"^Encontrado: SHA-256 [0-9a-f]{4}…[0-9a-f]{4}\. Esperado: [0-9a-f]{4}…[0-9a-f]{4}\. Nada foi alterado\.$", vm.Card.Detail);
        Assert.Equal("A tradução 1.2.0 foi feita para outra versão do jogo. Procure uma pasta com a versão certa, ou veja no leia-me qual é e onde conseguir.", vm.Note);
        Assert.Equal(InstallerApp.WrongVersion, vm.ExitCode);
        Assert.False(vm.InstallCommand.CanExecute(null));
        Assert.True(vm.BrowseCommand.CanExecute(null));
        Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.False(Directory.Exists(Path.Combine(game.Game, PatchInstaller.BackupFolderName)));
    }

    [Fact]
    public async Task Janela_4_cita_a_versao_do_jogo_quando_o_patch_a_guarda()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/script.ypf"), [9]);
        using var package = PatchPackage.Open(game.BuildPackage(SyntheticGame.Info() with { GameVersion = " 1.00 " }));

        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);

        Assert.Equal(InstallerScreen.WrongVersion, vm.Screen);
        Assert.Equal("A tradução 1.2.0 foi feita para a versão 1.00 do jogo. Procure uma pasta com essa versão, ou veja no leia-me onde conseguir.", vm.Note);
    }

    [Fact]
    public async Task A_capa_so_chega_ao_modelo_quando_o_patch_a_leva()
    {
        using var game = new SyntheticGame();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2];
        using var with = PatchPackage.Open(game.BuildPackage(SyntheticGame.Info() with { Cover = png }));
        using var without = PatchPackage.Open(game.BuildPackage());

        var withCover = await StartedAsync(new PatchEngine(with), game.Game);
        var withoutCover = await StartedAsync(new PatchEngine(without), game.Game);

        Assert.True(withCover.HasCover);
        Assert.Equal(png, withCover.CoverBytes);
        Assert.False(withoutCover.HasCover);
        Assert.Null(withoutCover.CoverBytes);
    }

    [Fact]
    public async Task Janela_4_com_arquivo_que_falta_e_com_mais_de_um_diferente_conta_os_outros()
    {
        using var game = new SyntheticGame();
        File.Delete(Path.Combine(game.Game, "Game/Game.exe"));
        File.WriteAllBytes(Path.Combine(game.Game, "Game/script.ypf"), [9]);
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);

        Assert.Equal(InstallerScreen.WrongVersion, vm.Screen);
        Assert.Contains("Mais 1 arquivo também não confere.", vm.Card!.Detail);
        Assert.Contains("Nada foi alterado.", vm.Card.Detail);
    }

    [Fact]
    public async Task Janela_4_escolher_outra_pasta_leva_a_janela_1_quando_a_pasta_serve()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/Game.exe"), [1]);
        using var other = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);
        Assert.Equal(InstallerScreen.WrongVersion, vm.Screen);
        vm.PickFolder = () => Task.FromResult<string?>(other.Game);

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Folder, vm.Screen);
        Assert.Equal(other.Game, vm.FolderPath);
        Assert.True(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sair_na_janela_4_fecha_com_o_codigo_3()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/Game.exe"), [1]);
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(InstallerApp.WrongVersion, vm.ExitCode);
    }

    // ---- 5 · Já instalada

    private static async Task<InstallerViewModel> InstalledAsync(SyntheticGame game, PatchPackage package, FakeShell? shell = null)
    {
        PatchInstaller.Install(game.Game, package, null, Ct);
        return await StartedAsync(new PatchEngine(package), game.Game, shell);
    }

    [Fact]
    public async Task Janela_5_mostra_a_versao_a_data_dos_originais_e_o_que_volta()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = await InstalledAsync(game, package);

        Assert.Equal(InstallerScreen.AlreadyInstalled, vm.Screen);
        Assert.Equal("A tradução 1.2.0 já está instalada", vm.Title);
        Assert.Matches(@"^Os arquivos originais, guardados em \d{2}/\d{2} às \d{2}:\d{2}, estão inteiros\. Voltar ao original apaga a tradução do jogo e não mexe nos seus saves\.$", vm.Intro);
        Assert.Equal(InstallerCardKind.Ok, vm.Card!.Kind);
        Assert.Equal("3 arquivos para devolver", vm.Card.Title);
        Assert.Contains("voltam byte a byte.", vm.Card.Detail);
        Assert.Contains("fonte.dat sai da pasta.", vm.Card.Detail);
        Assert.Equal(InstallerApp.Ok, vm.ExitCode);
        Assert.True(vm.RevertCommand.CanExecute(null));
        Assert.False(vm.InstallCommand.CanExecute(null));
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
    public async Task Janela_5_voltar_ao_original_mostra_uma_etapa_com_andamento()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var engine = new HookedEngine(new PatchEngine(package))
        {
            After = p =>
            {
                if (p is { Phase: PatchPhase.Restore, Done: 1, Path: not null })
                {
                    reached.Set();
                    release.Wait(TimeSpan.FromMinutes(3));
                }
            },
        };
        var vm = await StartedAsync(engine, game.Game);

        var revert = vm.RevertCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), Ct);

        Assert.Equal(InstallerScreen.Installing, vm.Screen);
        Assert.Equal("Voltando ao jogo original", vm.Title);
        Assert.Equal("Devolver os originais", Assert.Single(vm.Steps).Title);
        Assert.Equal(InstallStepState.Running, vm.Steps[0].State);
        Assert.Equal("33%", vm.Steps[0].Detail);
        Assert.False(vm.CancelInstallCommand.CanExecute(null));

        release.Set();
        await revert;
        Assert.Equal(InstallerScreen.Reverted, vm.Screen);
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
    public async Task Janela_5_com_todos_os_arquivos_mexidos_nao_promete_devolver_nada()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        foreach (var file in new[] { "Game/script.ypf", "Game/Game.exe", SyntheticGame.CreatedFile })
        {
            File.WriteAllBytes(Path.Combine(game.Game, file), [7]);
        }

        var vm = await StartedAsync(new PatchEngine(package), game.Root, folder: game.Game);

        Assert.Equal(InstallerScreen.AlreadyInstalled, vm.Screen);
        Assert.Equal("Nenhum arquivo para devolver", vm.Card!.Title);
        Assert.Contains("ficam como estão: mudaram depois da instalação.", vm.Card.Detail);
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

    [Fact]
    public async Task Fechar_na_janela_5_sai_com_0_sem_mexer_no_jogo()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var vm = await InstalledAsync(game, package);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        var before = SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf"));

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(InstallerApp.Ok, vm.ExitCode);
        Assert.Equal(before, SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.Equal("1.2.0", PatchInstaller.InstalledVersion(game.Game));
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

    [Fact]
    public async Task Erro_numa_engine_qualquer_nao_derruba_a_janela()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var engine = new HookedEngine(new PatchEngine(package)) { FailInstallWith = new IOException("disco cheio") };
        var vm = await StartedAsync(engine, game.Game);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Stopped, vm.Screen);
        Assert.Equal("disco cheio", vm.Card!.Detail);
        Assert.Equal(InstallerApp.Failed, vm.ExitCode);
    }

    [Fact]
    public async Task Erro_ao_voltar_ao_original_mostra_a_janela_de_erro_do_original()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        var engine = new HookedEngine(new PatchEngine(package)) { FailUninstallWith = new PatchException("A cópia sumiu.") };
        var vm = await StartedAsync(engine, game.Game);

        await vm.RevertCommand.ExecuteAsync(null);

        Assert.Equal(InstallerScreen.Stopped, vm.Screen);
        Assert.Equal("A volta ao original não terminou", vm.Title);
        Assert.Equal("Não foi possível voltar ao jogo original", vm.Card!.Title);
        Assert.Equal("A cópia sumiu.", vm.Card.Detail);
    }

    // ---- Instalador vazio e dados da coluna

    [Fact]
    public async Task Instalador_sem_patch_mostra_a_janela_de_erro_e_nao_faz_nada()
    {
        var vm = new InstallerViewModel(null, new FakeShell(), new InstallerStart(null, Path.GetTempPath(), null));

        await vm.StartAsync();

        Assert.Equal(InstallerScreen.Stopped, vm.Screen);
        Assert.Equal("Este instalador está vazio", vm.Title);
        Assert.Equal(InstallerCardKind.Error, vm.Card!.Kind);
        Assert.Equal("Tradução PT-BR", vm.WindowTitle);
        Assert.Equal(InstallerApp.Failed, vm.ExitCode);
        Assert.False(vm.ShowRetry);
        Assert.False(vm.BrowseCommand.CanExecute(null));
    }

    [Fact]
    public void A_coluna_da_esquerda_leva_o_nome_do_jogo_a_versao_e_os_creditos()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var vm = Create(new PatchEngine(package), game.Game);

        Assert.Equal("Jogo Sintético", vm.GameName);
        Assert.Equal("Tradução PT-BR · Jogo Sintético", vm.WindowTitle);
        Assert.Equal("Tradução para português do Brasil\nVersão 1.2.0 · Tradução: Equipe", vm.SideInfo);
    }

    [Fact]
    public void Sem_creditos_a_linha_da_versao_fica_so_com_a_versao()
    {
        using var game = new SyntheticGame();
        using var stream = new MemoryStream();
        PatchBuilder.Build(new PatchInfo("Jogo", "2.0.0", null, null, null), game.Sources(), stream, cancellationToken: Ct);
        stream.Position = 0;
        using var package = PatchPackage.Open(stream);

        var vm = Create(new PatchEngine(package), game.Game);

        Assert.Equal("Tradução para português do Brasil\nVersão 2.0.0", vm.SideInfo);
        Assert.False(vm.HasReadMe);
    }

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
