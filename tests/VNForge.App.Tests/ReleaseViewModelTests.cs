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
            new ReleaseFile("Amaoto-PT-BR-1.2.0.exe", 5_347_737, new string('a', 64)),
            new ReleaseFile("Amaoto-PT-BR-1.2.0.vnfpatch", 4_508_876, new string('b', 64)),
            new ReleaseFile("LEIA-ME.txt", 2_048, null),
            new ReleaseFile("CREDITOS.txt", 1_024, null),
            new ReleaseFile("MUDANCAS.txt", 6_144, null),
            new ReleaseFile("LICENCAS.txt", 9_000, null),
        ],
        [
            new ReleaseGameFile("Game/script.ypf", 224_395_264, 3_984_588, 224_400_000, false),
            new ReleaseGameFile("Game/Game.exe", 2_202_009, 14_336, 2_202_100, false),
        ],
        new PatchVerification(2, 2, 0),
        new ChangesSinceTest(ChangesSinceTestState.Changed, null, false, []));

    internal sealed class FakePublisher(FakeTimeProvider? clock = null) : IReleasePublisher
    {
        public ReleaseReadiness Readiness { get; set; } = Ready();

        public Exception? Failure { get; set; }

        public Func<CancellationToken, bool>? Blocker { get; set; }

        public List<ReleaseOptions> Created { get; } = [];

        public string? OutputOverride { get; set; }

        public ReleaseReadiness Inspect(string projectRoot, CancellationToken cancellationToken) => Readiness;

        public string DefaultInstallerName(string projectRoot, string version) => "Amaoto-PT-BR-" + version;

        public ReleaseResult Create(string projectRoot, ReleaseOptions options, IProgress<ReleaseStep>? steps, CancellationToken cancellationToken)
        {
            Created.Add(options);
            foreach (var step in Enum.GetValues<ReleaseStep>())
            {
                steps?.Report(step);
                clock?.Advance(TimeSpan.FromMilliseconds(500 * ((int)step + 1)));
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
            File.WriteAllText(Path.Combine(options.OutputDirectory!, "LEIA-ME.txt"), "Amaoto no Hakoniwa\r\nCOMO INSTALAR\r\n");
            return Result(options.OutputDirectory!, options.Version);
        }
    }

    internal sealed class RecordingStarter : IGameProcessStarter
    {
        public List<string> Started { get; } = [];

        public void Start(string executablePath, string workingDirectory) => Started.Add(executablePath);
    }

    internal static async Task<(ReleaseViewModel Vm, FakePublisher Publisher, TempFolder Temp, FakeTimeProvider Clock)> OpenAsync(ReleaseReadiness? readiness = null)
    {
        var temp = new TempFolder();
        var clock = new FakeTimeProvider();
        var publisher = new FakePublisher(clock) { Readiness = readiness ?? Ready() };
        var vm = new ReleaseViewModel(new BackgroundTaskService(), publisher, clock, new RecordingStarter(), _ => { }) { Project = Summary(temp.Path) };
        await vm.ReloadAsync();
        return (vm, publisher, temp, clock);
    }

    [Fact]
    public async Task Abre_pronta_com_a_versao_sugerida_o_instalador_e_a_pasta_de_saida()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        Assert.Equal(ReleasePhase.Ready, vm.Phase);
        Assert.Equal("1.2.0", vm.Version);
        Assert.Equal("Patch anterior: 1.1.0, de 02/10", vm.VersionHelp);
        Assert.Equal("Última versão: 1.1.0 · 02/10", vm.LastVersionText);
        Assert.StartsWith("Amaoto-PT-BR-1.2.0", vm.InstallerName, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(temp.Path, "releases", "1.2.0"), vm.OutputDirectory);
        Assert.Equal(GameState.Translated, vm.BannerState);
        Assert.Equal("A tradução pode virar uma versão", vm.BannerTitle);
        Assert.Equal("400 linhas · 25% aprovadas · versão do jogo conferida pelo checksum", vm.BannerMessage);
        Assert.True(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Os_tres_checks_dizem_o_que_confere()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        Assert.Collection(
            vm.Checks,
            c => Assert.Equal(("Conferir sem erros", true), (c.Text, c.IsOk)),
            c => Assert.Equal(($"Testado no jogo em {LocalTested:dd/MM} às {LocalTested:HH:mm}", true), (c.Text, c.IsOk)),
            c => Assert.Equal(("Jogo de referência confere", true), (c.Text, c.IsOk)));
    }

    [Fact]
    public async Task Sem_build_o_jogo_so_e_conferido_ao_gerar_e_sem_teste_o_check_fica_aberto()
    {
        var read = Ready(previous: null, reference: ReleaseReference.Unknown) with { TestedAt = null };
        var (vm, _, temp, _) = await OpenAsync(read);
        using var _t = temp;

        Assert.Equal("1.0.0", vm.Version);
        Assert.Equal("Primeira versão do patch", vm.VersionHelp);
        Assert.Equal(string.Empty, vm.LastVersionText);
        Assert.False(vm.ShowChanges);
        Assert.False(vm.Checks[1].IsOk);
        Assert.True(vm.Checks[1].IsPending);
        Assert.Equal("Jogo de referência confere ao gerar", vm.Checks[2].Text);
        Assert.Contains("conferido pelo checksum ao gerar", vm.BannerMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mudou_desde_o_patch_anterior_lista_as_cenas_do_core()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        Assert.True(vm.ShowChanges);
        Assert.Equal("Mudou desde o patch 1.1.0", vm.ChangesTitle);
        Assert.Equal("41 linhas em 2 cenas · vai para MUDANCAS.txt", vm.ChangesSubtitle);
        Assert.Equal(["Cena 1", "Cena 2"], vm.Scenes.Select(s => s.Title));
        Assert.Equal(["30 linhas", "11 linhas"], vm.Scenes.Select(s => s.Detail));
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

    [Theory]
    [InlineData("1.2.0", true)]
    [InlineData("v2_beta-1", true)]
    [InlineData("", false)]
    [InlineData("1 2", false)]
    [InlineData(".1", false)]
    public async Task Versao_invalida_trava_o_botao_e_mostra_o_motivo(string version, bool valid)
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        vm.Version = version;

        Assert.Equal(valid, vm.GenerateCommand.CanExecute(null));
        Assert.Equal(!valid && version.Length > 0, vm.HasVersionError);
    }

    [Fact]
    public async Task Erros_da_conferencia_travam_o_botao_e_a_faixa_manda_para_os_erros()
    {
        var (vm, _, temp, _) = await OpenAsync(Ready(errors: 3));
        using var _t = temp;
        var asked = 0;
        vm.ErrorsRequested += (_, _) => asked++;

        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.Equal(GameState.Blocked, vm.BannerState);
        Assert.Equal("A Conferência tem erros", vm.BannerTitle);
        Assert.Equal("Conferir com 3 erros", vm.Checks[0].Text);
        vm.ShowErrorsCommand.Execute(null);
        Assert.Equal(1, asked);
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
        Assert.Equal("Verificada numa cópia limpa: 2 de 2 arquivos idênticos ao build. Desinstalar devolve os originais byte a byte.", vm.BannerMessage);
        Assert.Single(notices);
        Assert.False(vm.ShowReceived);
        Assert.Contains("COMO INSTALAR", vm.ReadMeText, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', vm.ReadMeText);
    }

    [Fact]
    public async Task Os_seis_passos_seguem_a_ordem_do_core_e_ganham_o_tempo_de_cada_um()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(
            ["Conferir a tradução", "Montar o build", "Calcular as diferenças e gravar o patch", "Verificar numa cópia limpa", "Montar o instalador", "Gravar leia-me, créditos e mudanças"],
            vm.Steps.Select(s => s.Label));
        Assert.All(vm.Steps, s => Assert.True(s.IsDone));
        Assert.Equal(["0,5 s", "1,0 s", "1,5 s", "2,0 s", "2,5 s", "3,0 s"], vm.Steps.Select(s => s.TimeText));
        Assert.Equal("2,0 s", vm.VerificationTime);
    }

    [Fact]
    public async Task A_versao_pronta_lista_os_arquivos_o_checksum_dos_pacotes_e_a_tabela_do_patch()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(
            ["Amaoto-PT-BR-1.2.0.exe", "Amaoto-PT-BR-1.2.0.vnfpatch", "LEIA-ME.txt", "CREDITOS.txt", "MUDANCAS.txt", "LICENCAS.txt"],
            vm.Files.Select(f => f.Name));
        Assert.Equal([true, true, false, false, false, false], vm.Files.Select(f => f.HasChecksum));
        Assert.Equal(["5,1 MB", "4,3 MB", "2 KB", "1 KB", "6 KB", "9 KB"], vm.Files.Select(f => f.SizeText));
        Assert.Equal(["Game/script.ypf", "Game/Game.exe"], vm.GameFiles.Select(f => f.Path));
        Assert.Equal(["214,0 MB", "2,1 MB"], vm.GameFiles.Select(f => f.OriginalText));
        Assert.Equal(["3,8 MB", "14 KB"], vm.GameFiles.Select(f => f.PatchText));
        Assert.Equal(["1,8%", "0,7%"], vm.GameFiles.Select(f => f.RatioText));
        Assert.Equal("2 arquivos tocados", vm.GameFilesTotal);
        Assert.StartsWith("Cópia limpa do jogo de referência: 2 arquivos alterados, todos idênticos ao build.", vm.VerificationText, StringComparison.Ordinal);
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
        File.WriteAllText(Path.Combine(vm.OutputDirectory, "Amaoto-PT-BR-1.2.0.exe"), "MZ");

        vm.TestInstallerCommand.Execute(null);
        vm.OpenFolderCommand.Execute(null);

        Assert.Equal([Path.Combine(vm.OutputDirectory, "Amaoto-PT-BR-1.2.0.exe")], starter.Started);
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
    public async Task Falha_esperada_do_core_vira_a_mensagem_da_pagina_e_deixa_tentar_de_novo()
    {
        var (vm, publisher, temp, _) = await OpenAsync();
        using var _t = temp;
        publisher.Failure = new VNForgeException("Não achei o executável do instalador.");

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(ReleasePhase.Ready, vm.Phase);
        Assert.Equal("Não achei o executável do instalador.", vm.ErrorMessage);
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
    public async Task Jogo_fora_da_versao_de_referencia_ja_na_leitura_abre_a_pagina_travada()
    {
        var read = Ready(reference: ReleaseReference.Differs, problem: "O jogo mudou desde a extração.");
        var (vm, _, temp, _) = await OpenAsync(read);
        using var _t = temp;
        var asked = 0;
        vm.GameFolderRequested += (_, _) => asked++;

        Assert.Equal(ReleasePhase.Blocked, vm.Phase);
        Assert.Equal(@"D:\VNs\Amaoto no Hakoniwa", vm.GameFolder);
        Assert.False(vm.ShowProblemDetails);
        vm.ToggleProblemDetailsCommand.Execute(null);
        Assert.True(vm.ShowProblemDetails);
        vm.ChooseGameFolderCommand.Execute(null);
        Assert.Equal(1, asked);
        Assert.Equal("Jogo de referência não confere", vm.Checks[2].Text);
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

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(812, "812 B")]
    [InlineData(14_336, "14 KB")]
    [InlineData(5_347_737, "5,1 MB")]
    public void Formata_o_tamanho_em_portugues(long bytes, string expected) =>
        Assert.Equal(expected, ReleaseViewModel.FormatSize(bytes));

    [Fact]
    public async Task Trocar_de_projeto_limpa_a_pagina_e_so_le_o_disco_quando_a_janela_pede()
    {
        var (vm, _, temp, _) = await OpenAsync();
        using var _t = temp;
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(ReleasePhase.Done, vm.Phase);

        vm.Project = Summary(temp.Path) with { Name = "Outro jogo" };

        Assert.Equal(ReleasePhase.Ready, vm.Phase);
        Assert.Empty(vm.Files);
        Assert.Empty(vm.Checks);
        Assert.Equal(string.Empty, vm.Version);
    }
}
