using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;
using VNForge.Patching;

namespace VNForge.App.Tests;

[Trait("Risco", "R1")]
public sealed class ReleaseViewModelTests
{

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

    [Theory]
    [InlineData("jogo", true)]
    [InlineData("jogo/releases/1.2.0", true)]
    [InlineData("jogo2", false)]
    [InlineData("releases/1.2.0", false)]
    public async Task Pasta_de_saida_nao_pode_ser_a_do_jogo_nem_ficar_dentro_dela(string output, bool refused)
    {
        using var temp = new TempFolder();
        var game = Path.Combine(temp.Path, "jogo");
        var publisher = new FakePublisher { Readiness = Ready() with { GameFolder = game } };
        var vm = new ReleaseViewModel(new BackgroundTaskService(), publisher, new FakeTimeProvider(), new RecordingStarter(), _ => { }) { Project = Summary(temp.Path) };
        await vm.ReloadAsync();

        vm.OutputDirectory = Path.Combine(temp.Path, output);

        Assert.Equal(refused, vm.HasOutputError);
        Assert.Equal(!refused, vm.GenerateCommand.CanExecute(null));
        Assert.Equal(refused ? "A pasta de saída não pode ser a pasta do jogo nem ficar dentro dela. Escolha outra." : null, vm.OutputError);
    }
}
