using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;
using VNForge.Core.Qa;
using VNForge.Patching;

namespace VNForge.App.Tests;

public sealed class ReleaseViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime LocalTested => new DateTimeOffset(2026, 11, 8, 19, 42, 0, TimeSpan.Zero).ToLocalTime().DateTime;

    internal static ProjectSummary Summary(string root) =>
        new("Amaoto no Hakoniwa", root, "yuris", 500, "cp1252", "claude", 312, new StatusCounts(0, 0, 100, 200, 100, 0));

    internal static ReleaseReadiness Ready(string? previous = "1.1.0", int errors = 0, ReleaseReference reference = ReleaseReference.Matches, string? problem = null) => new(
        @"D:\VNs\Amaoto no Hakoniwa",
        previous,
        previous is null ? null : new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
        errors,
        0,
        new DateTimeOffset(2026, 11, 8, 19, 42, 0, TimeSpan.Zero),
        reference,
        problem,
        previous is null
            ? ChangesSinceTest.None
            : new ChangesSinceTest(ChangesSinceTestState.Changed, null, false, [new ChangedScene("yst00001", 1, 30, []), new ChangedScene("yst00002", 2, 11, [])]));

    internal static ReleaseResult Result(string output, string version = "1.2.0") => new(
        version,
        "1.1.0",
        output,
        [
            new ReleaseFile("Amaoto-no-Hakoniwa-PT-BR-1.2.0.exe", 5_347_737, new string('a', 64)),
            new ReleaseFile("Amaoto-no-Hakoniwa-PT-BR-1.2.0.vnfpatch", 4_508_876, new string('b', 64)),
            new ReleaseFile("LEIA-ME.txt", 2_048, null),
            new ReleaseFile("CREDITOS.txt", 1_024, null),
            new ReleaseFile("MUDANCAS.txt", 6_144, null),
            new ReleaseFile("LICENCAS.txt", 9_000, null),
        ],
        GameFilesOfQuadro,
        new PatchVerification(4, 4, 0),
        new ChangesSinceTest(ChangesSinceTestState.Changed, null, false, []));

    /// <summary>Os quatro arquivos que o quadro mostra em "O que o patch muda no jogo".</summary>
    internal static IReadOnlyList<ReleaseGameFile> GameFilesOfQuadro { get; } =
    [
        new ReleaseGameFile("Game/script.ypf", 224_395_264, 3_984_588, 224_400_000, false),
        new ReleaseGameFile("Game/Game.exe", 2_202_009, 14_336, 2_202_100, false),
        new ReleaseGameFile("Game/font.ypf", 50_960_000, 431_104, 50_961_000, false),
        new ReleaseGameFile("Game/system.ypf", 9_805_000, 63_900, 9_806_000, false),
    ];

    /// <summary>
    /// A leitura com os dados do quadro (#540): 41 linhas em 6 cenas, as três falas de exemplo e o checksum do <c>Game.exe</c> que não confere.
    /// Só os testes de render a usam; os valores são dado de teste.
    /// </summary>
    internal static ReleaseReadiness ReadyLikeQuadro(ReleaseReference reference = ReleaseReference.Matches) => ReadyWithPreviousPatch() with
    {
        TestedAt = new DateTimeOffset(new DateTime(2026, 11, 8, 19, 42, 0, DateTimeKind.Local)),
        Reference = reference,
        GameVersion = reference == ReleaseReference.Differs ? "1.01" : null,
        ReferenceVersion = reference == ReleaseReference.Differs ? "1.00" : null,
        ReferenceProblem = reference == ReleaseReference.Differs ? "O jogo mudou desde a extração. Arquivos alterados: Game/Game.exe." : null,
        ChangedFiles = reference == ReleaseReference.Differs
            ? [new ChangedGameFile("Game/Game.exe", "3f9a" + new string('0', 56) + "c21d", "77b0" + new string('1', 56) + "e04a")]
            : null,
        Changes = new ChangesSinceTest(
            ChangesSinceTestState.Changed,
            null,
            false,
            [
                new ChangedScene("yst00001", 1, 30, []),
                new ChangedScene("yst00002", 2, 1, []),
                new ChangedScene("yst00003", 3, 1, []),
                new ChangedScene("yst00004", 4, 5, []),
                new ChangedScene("yst00005", 5, 3, []),
                new ChangedScene("yst00006", 6, 1, []),
            ]),
        ChangeExamples =
        [
            new ChangeExample(1, 2, "Lá fora, a chuva ainda não parou.", "Lá fora, a chuva não parou desde cedo."),
            new ChangeExample(2, 14, "Aoi: Vamos logo.", "Aoi: Vamos, antes que o sinal toque.", "Manhã de chuva"),
            new ChangeExample(3, 31, "Professor: Silêncio.", "Professor: Silêncio, por favor.", "Sala de aula"),
        ],
    };

    /// <summary>A leitura de um projeto que já lançou a 1.1.0: dela vêm a estimativa dos tamanhos e a tabela de arquivos.</summary>
    internal static ReleaseReadiness ReadyWithPreviousPatch() => Ready() with
    {
        PreviousFiles = Result("anterior", "1.1.0").Files,
        PreviousGameFiles = GameFilesOfQuadro,
        GameFileCount = 312,
    };

    internal sealed class FakePublisher(FakeTimeProvider? clock = null) : IReleasePublisher
    {
        public ReleaseReadiness Readiness { get; set; } = Ready();

        public Exception? Failure { get; set; }

        public Func<CancellationToken, bool>? Blocker { get; set; }

        public List<ReleaseOptions> Created { get; } = [];

        public string? OutputOverride { get; set; }

        /// <summary>Quanto cada passo leva no relógio falso; o padrão é meio segundo vezes a posição do passo.</summary>
        public Func<ReleaseStep, TimeSpan>? Duration { get; set; }

        /// <summary>O conteúdo do LEIA-ME.txt que o "Core" falso grava; o padrão é um texto curto.</summary>
        public string ReadMe { get; set; } = "Amaoto no Hakoniwa\r\nCOMO INSTALAR\r\n";

        public ReleaseReadiness Inspect(string projectRoot, CancellationToken cancellationToken) => Readiness;

        public string DefaultInstallerName(string projectRoot, string version) => "Amaoto-no-Hakoniwa-PT-BR-" + version;

        public ReleaseResult Create(string projectRoot, ReleaseOptions options, IProgress<ReleaseProgress>? steps, CancellationToken cancellationToken)
        {
            Created.Add(options);
            foreach (var step in Enum.GetValues<ReleaseStep>())
            {
                steps?.Report(new ReleaseProgress(step));
                switch (step)
                {
                    case ReleaseStep.Build:
                        steps?.Report(new ReleaseProgress(step, Detail: "308 de 312 scripts reaproveitados"));
                        break;
                    case ReleaseStep.Diff:
                        steps?.Report(new ReleaseProgress(step, Detail: "4 arquivos"));
                        break;
                    case ReleaseStep.Patch:
                        steps?.Report(new ReleaseProgress(step, Fraction: 0.63, Item: "Game/script.ypf"));
                        break;
                }

                clock?.Advance(Duration?.Invoke(step) ?? TimeSpan.FromMilliseconds(500 * ((int)step + 1)));
                if (Blocker?.Invoke(cancellationToken) == true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (Failure is not null && step == ReleaseStep.Build)
                {
                    throw Failure;
                }
            }

            Directory.CreateDirectory(options.OutputDirectory!);
            File.WriteAllText(Path.Combine(options.OutputDirectory!, "LEIA-ME.txt"), ReadMe);
            return Result(options.OutputDirectory!, options.Version);
        }
    }

    internal sealed class RecordingStarter : IGameProcessStarter
    {
        public List<string> Started { get; } = [];

        public void Start(string executablePath, string workingDirectory) => Started.Add(executablePath);
    }

    /// <summary>As 48.211 linhas e os 34% aprovados que o banner do quadro mostra; só os testes de render usam.</summary>
    internal static StatusCounts QuadroCounts { get; } = new(3_811, 8_000, 20_000, 0, 16_400);

    internal static async Task<(ReleaseViewModel Vm, FakePublisher Publisher, TempFolder Temp, FakeTimeProvider Clock)> OpenAsync(ReleaseReadiness? readiness = null, StatusCounts? counts = null)
    {
        var temp = new TempFolder();
        var clock = new FakeTimeProvider();
        var publisher = new FakePublisher(clock) { Readiness = readiness ?? Ready() };
        var vm = new ReleaseViewModel(new BackgroundTaskService(), publisher, clock, new RecordingStarter(), _ => { }) { Project = counts is null ? Summary(temp.Path) : Summary(temp.Path) with { Counts = counts } };
        await vm.ReloadAsync();
        return (vm, publisher, temp, clock);
    }

    [Fact]
    public async Task O_cartao_de_checksum_mostra_as_versoes_do_jogo_quando_o_core_as_conhece_e_nada_quando_nao()
    {
        var (vm, _, temp, _) = await OpenAsync(ReadyLikeQuadro(ReleaseReference.Differs));
        using var _t = temp;

        Assert.Equal(@"D:\VNs\Amaoto no Hakoniwa (versão 1.01)", vm.GameFolderLabel);
        Assert.StartsWith("Aponte a pasta da versão original que você usou para extrair o texto (1.00). Se o jogo", vm.WhatToDoText, StringComparison.Ordinal);

        var (plain, _, plainTemp, _) = await OpenAsync(Ready(reference: ReleaseReference.Differs));
        using var _p = plainTemp;

        Assert.Equal(@"D:\VNs\Amaoto no Hakoniwa", plain.GameFolderLabel);
        Assert.StartsWith("Aponte a pasta da versão original que você usou para extrair o texto. Se o jogo", plain.WhatToDoText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_legenda_do_campo_e_Opcional_vazio_e_Jogo_de_referencia_preenchido()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;
        Assert.Equal("Opcional", vm.GameVersionLegend);

        vm.GameVersion = "1.00";

        Assert.Equal("Jogo de referência", vm.GameVersionLegend);
    }

    [Fact]
    public async Task Trocar_a_versao_leva_a_pasta_e_o_instalador_junto_ate_o_usuario_escolher_os_dele()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        vm.Version = "1.3.0";
        Assert.Equal(Path.Combine(temp.Path, "releases", "1.3.0"), vm.OutputDirectory);

        vm.OutputDirectory = Path.Combine(temp.Path, "minha-pasta");
        vm.Version = "1.4.0";
        Assert.Equal(Path.Combine(temp.Path, "minha-pasta"), vm.OutputDirectory);
    }

    [Fact]
    public async Task Os_atalhos_da_pagina_avisam_quem_escuta_e_nao_falham_sem_ninguem()
    {
        var (vm, _, temp, _) = await OpenAsync(Ready());
        using var _t = temp;
        vm.ShowTestCommand.Execute(null);
        vm.ShowErrorsCommand.Execute(null);
        vm.ChooseGameFolderCommand.Execute(null);
        var avisados = new List<string>();
        vm.TestRequested += (_, _) => avisados.Add("teste");
        vm.ErrorsRequested += (_, _) => avisados.Add("erros");
        vm.GameFolderRequested += (_, _) => avisados.Add("pasta");

        vm.ShowTestCommand.Execute(null);
        vm.ShowErrorsCommand.Execute(null);
        vm.ChooseGameFolderCommand.Execute(null);

        Assert.Equal(["teste", "erros", "pasta"], avisados);
    }

    [Fact]
    public async Task Gerar_roda_o_core_com_as_opcoes_da_tela_e_termina_na_versao_pronta()
    {
        var (vm, publisher, temp, _) = await OpenAsync();
        using var _t = temp;
        var notices = new List<string>();
        vm.NoticeRaised += (_, text) => notices.Add(text);
        vm.Credits = "Tradução: Equipe Chuva Mansa · Revisão: Hana";
        vm.InstallerName = "Meu-Instalador" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

        await vm.GenerateCommand.ExecuteAsync(null);

        var options = Assert.Single(publisher.Created);
        Assert.Equal("1.2.0", options.Version);
        Assert.Equal("Meu-Instalador", options.InstallerName);
        Assert.Equal("Tradução: Equipe Chuva Mansa · Revisão: Hana", options.Credits);
        Assert.Equal(Path.Combine(temp.Path, "releases", "1.2.0"), options.OutputDirectory);
        Assert.False(options.AllowWarnings);

        Assert.Equal(ReleasePhase.Done, vm.Phase);
        Assert.Equal("Versão 1.2.0 pronta", vm.BannerTitle);
        Assert.Equal("Verificada numa cópia limpa: 4 de 4 arquivos idênticos ao build. Desinstalar devolve os originais byte a byte.", vm.BannerMessage);
        Assert.Single(notices);
        Assert.False(vm.ShowReceived);
        Assert.Contains("COMO INSTALAR", vm.ReadMeText, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', vm.ReadMeText);
    }

    [Fact]
    public async Task Cada_etapa_mostra_o_detalhe_que_o_core_achou_e_a_atual_o_andamento_do_arquivo()
    {
        var (vm, publisher, temp, _) = await OpenAsync();
        using var _t = temp;
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        publisher.Blocker = _ =>
        {
            if (++calls == 4)
            {
                reached.Set();
                release.Wait(TimeSpan.FromMinutes(3));
            }

            return false;
        };

        var running = vm.GenerateCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), Ct);
        try
        {
            Assert.Equal(
                ["0,5 s", "1,0 s · 308 de 312 scripts reaproveitados", "1,5 s · 4 arquivos", "63% · Game/script.ypf", string.Empty, string.Empty],
                vm.Steps.Select(s => s.StatusText));
            var patch = vm.Steps[3];
            Assert.True(patch.IsRunning);
            Assert.False(patch.IsIndeterminate);
            Assert.Equal(63, patch.Percent);
            Assert.True(vm.Steps[2].IsIndeterminate);
        }
        finally
        {
            release.Set();
            await running;
        }

        Assert.Equal("3,0 s", vm.Steps[5].StatusText);
    }

    [Fact]
    public async Task Copiar_checksum_entrega_o_sha_inteiro_do_pacote()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;
        await vm.GenerateCommand.ExecuteAsync(null);
        var copied = new List<string>();
        vm.CopyRequested += (_, text) => copied.Add(text);

        vm.CopyChecksumCommand.Execute(vm.Files[1]);
        vm.CopyChecksumCommand.Execute(vm.Files[2]);

        Assert.Equal([new string('b', 64)], copied);
    }

    [Fact]
    public async Task Testar_o_instalador_abre_o_executavel_gerado_e_abrir_a_pasta_abre_a_saida()
    {
        using var temp = new TempFolder();
        var starter = new RecordingStarter();
        var opened = new List<string>();
        var publisher = new FakePublisher();
        var vm = new ReleaseViewModel(new BackgroundTaskService(), publisher, new FakeTimeProvider(), starter, opened.Add) { Project = Summary(temp.Path) };
        await vm.ReloadAsync();
        await vm.GenerateCommand.ExecuteAsync(null);
        File.WriteAllText(Path.Combine(vm.OutputDirectory, "Amaoto-no-Hakoniwa-PT-BR-1.2.0.exe"), "MZ");

        vm.TestInstallerCommand.Execute(null);
        vm.OpenFolderCommand.Execute(null);

        Assert.Equal([Path.Combine(vm.OutputDirectory, "Amaoto-no-Hakoniwa-PT-BR-1.2.0.exe")], starter.Started);
        Assert.Equal([vm.OutputDirectory], opened);
    }

    [Fact]
    public async Task Cancelar_no_meio_volta_a_tela_pronta_sem_marcar_a_versao()
    {
        var (vm, publisher, temp, _) = await OpenAsync();
        using var _t = temp;
        publisher.Blocker = ct =>
        {
            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            return true;
        };

        var running = vm.GenerateCommand.ExecuteAsync(null);
        await Task.Run(() => SpinWait.SpinUntil(() => vm.CancelGenerationCommand.CanExecute(null), TimeSpan.FromSeconds(10)), Ct);
        Assert.Equal(ReleasePhase.Generating, vm.Phase);
        Assert.Equal("Gerando...", vm.GenerateLabel);
        Assert.False(vm.GenerateCommand.CanExecute(null));

        vm.CancelGenerationCommand.Execute(null);
        await running;

        Assert.Equal(ReleasePhase.Ready, vm.Phase);
        Assert.Contains("cancelada", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.All(vm.Steps, s => Assert.True(s.IsPending));
        Assert.True(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Jogo_fora_da_versao_de_referencia_ao_gerar_trava_a_pagina_com_o_motivo_do_core()
    {
        var (vm, publisher, temp, _) = await OpenAsync();
        using var _t = temp;
        publisher.Failure = new InjectionException("O jogo mudou desde a extração. Arquivos alterados: Game/Game.exe.");

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(ReleasePhase.Blocked, vm.Phase);
        Assert.Equal("O jogo desta pasta não é a versão de referência", vm.BannerTitle);
        Assert.Equal("O patch só vale para a versão que o projeto usou. Nada foi gerado.", vm.BannerMessage);
        Assert.Equal(GameState.Blocked, vm.BannerState);
        Assert.Contains("Game/Game.exe", vm.ProblemDetails, StringComparison.Ordinal);
        Assert.False(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Escolher_a_pasta_de_saida_usa_o_seletor_e_cancelar_nao_muda_nada()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;
        var before = vm.OutputDirectory;
        vm.Picker = new FakeFolderPicker(null);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.OutputDirectory);

        vm.Picker = new FakeFolderPicker(@"H:\Releases\Amaoto");
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        Assert.Equal(@"H:\Releases\Amaoto", vm.OutputDirectory);
    }

    [Theory]
    [InlineData("1.1.0", "1.2.0")]
    [InlineData("2.9", "2.10.0")]
    [InlineData("beta", "beta-2")]
    [InlineData(null, "1.0.0")]
    public void Sugere_a_proxima_versao(string? previous, string expected) =>
        Assert.Equal(expected, ReleaseViewModel.SuggestVersion(previous));

    [Fact]
    public async Task Trocar_de_projeto_no_meio_da_geracao_nao_mostra_a_versao_do_projeto_antigo_no_novo()
    {
        var (vm, publisher, temp, _) = await OpenAsync();
        using var _t = temp;
        var notices = new List<string>();
        vm.NoticeRaised += (_, text) => notices.Add(text);
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        publisher.Blocker = _ =>
        {
            if (++calls == 3)
            {
                reached.Set();
                release.Wait(TimeSpan.FromMinutes(3));
            }

            return false;
        };

        var running = vm.GenerateCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), Ct);
        vm.Project = Summary(temp.Path) with { Name = "Outro jogo", Root = temp.SubFolder("outro") };
        release.Set();
        await running;

        Assert.Equal(ReleasePhase.Ready, vm.Phase);
        Assert.Empty(vm.Files);
        Assert.Empty(vm.GameFiles);
        Assert.Equal(string.Empty, vm.ReadMeText);
        Assert.Empty(notices);
        Assert.All(vm.Steps, s => Assert.True(s.IsPending));
        Assert.DoesNotContain("pronta", vm.BannerTitle, StringComparison.Ordinal);
        Assert.False(vm.CancelGenerationCommand.CanExecute(null));
    }

    [Fact]
    public async Task Falha_do_core_marca_a_tarefa_da_barra_como_falha_e_a_pagina_mostra_o_erro()
    {
        using var temp = new TempFolder();
        var tasks = new BackgroundTaskService();
        var publisher = new FakePublisher { Failure = new VNForgeException("Não achei o executável do instalador.") };
        var vm = new ReleaseViewModel(tasks, publisher, new FakeTimeProvider(), new RecordingStarter(), _ => { }) { Project = Summary(temp.Path) };
        await vm.ReloadAsync();

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(tasks.Tasks).IsFailed);
        Assert.Equal("Não achei o executável do instalador.", vm.ErrorMessage);
    }
}
