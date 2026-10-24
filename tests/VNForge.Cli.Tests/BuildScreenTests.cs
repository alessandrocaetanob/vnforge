using System.Security.Cryptography;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using VNForge.Engines.YuRis;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary>#103: a tela Build e injeção faz build, injeção e restauração numa pasta de jogo sintética, com o Core e as engines reais.</summary>
public sealed class BuildScreenTests : IDisposable
{
    private const string Ysbin = "ysbin.ypf";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly string _projectPath;
    private readonly string _originalHash;
    private readonly LaunchRecorder _launcher = new();

    public BuildScreenTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        File.WriteAllBytes(Path.Combine(_game, Ysbin), ScriptArchive());
        _originalHash = Hash(Path.Combine(_game, Ysbin));
        _projectPath = Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");
    }

    public void Dispose() => _cli.Dispose();

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static byte[] ScriptArchive()
    {
        var script = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        script.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        script.AddCommand("GOSUB", 3, script.StringExpression("ES.CHAR.NAME"), script.StringExpression("美咲"));
        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").Build();
        return new SyntheticYpfBuilder(500)
            .AddFile("ysc.ybn", Yscm.Bytes)
            .AddFile("yst_list.ybn", list.Bytes)
            .AddFile("yst00001.ybn", script.Build().Bytes)
            .Build().Bytes;
    }

    private async Task<BuildViewModel> OpenProjectAsync()
    {
        var init = await _cli.RunAsync(["init", _projectPath, "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        var extract = await _cli.RunAsync(["extract", "--project", _projectPath], TestContext.Current.CancellationToken);
        Assert.Equal(0, extract.ExitCode);

        var vm = new BuildViewModel(new BackgroundTaskService(), [new YuRisEngine()], _launcher, new QaEngine([]));
        vm.Project = ProjectSummaryReader.Read(_projectPath);
        await vm.ReloadAsync();
        return vm;
    }

    private void Translate()
    {
        var project = ProjectFolder.Open(_projectPath);
        var units = project.LoadText("yst00001").Select(u => u with { Translation = "Good morning", Status = TranslationStatus.Translated }).ToList();
        project.SaveText("yst00001", units);
    }

    private static async Task ConfirmAsync(BuildViewModel vm) => await vm.ConfirmCommand.ExecuteAsync(null);

    [Fact]
    public async Task Build_injecao_e_restauracao_rodam_pelo_app_e_o_jogo_volta_ao_original()
    {
        var vm = await OpenProjectAsync();
        Translate();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.HasFacts);
        Assert.Contains(vm.Facts, f => f.Label == "Scripts" && f.Value.StartsWith("1 reconstruído de 1", StringComparison.Ordinal));
        Assert.Contains(vm.Facts, f => f.Label == Ysbin && f.Value.Contains("original", StringComparison.Ordinal));
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Gravado: ", StringComparison.Ordinal));
        Assert.NotEqual(_originalHash, Hash(Path.Combine(_projectPath, "build", Ysbin)));
        Assert.Equal(_originalHash, Hash(Path.Combine(_game, Ysbin)));
        var change = Assert.Single(vm.Changes);
        Assert.Equal(Ysbin, change.Path);

        vm.InjectCommand.Execute(null);
        await ConfirmAsync(vm);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Hash(Path.Combine(_projectPath, "build", Ysbin)), Hash(Path.Combine(_game, Ysbin)));
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(_projectPath, "backup")));
        Assert.Equal(_originalHash, Hash(Path.Combine(backup, Ysbin)));
        Assert.Equal(backup, vm.BackupLocation);

        vm.RestoreCommand.Execute(null);
        await ConfirmAsync(vm);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(_originalHash, Hash(Path.Combine(_game, Ysbin)));
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Restaurado: 1 arquivo(s)", StringComparison.Ordinal));

        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(_game, "MinhaVN.exe"), _launcher.Executable);
    }

    [Fact]
    public async Task Build_sem_traducao_gera_pacote_identico_e_nada_a_injetar()
    {
        var vm = await OpenProjectAsync();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(_originalHash, Hash(Path.Combine(_projectPath, "build", Ysbin)));
        Assert.Empty(vm.Changes);
        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.Contains("já estão iguais", vm.PlanMessage);
    }

    [Fact]
    public async Task Jogo_atualizado_depois_da_extracao_trava_a_injecao_e_diz_por_que()
    {
        var vm = await OpenProjectAsync();
        Translate();
        await vm.BuildCommand.ExecuteAsync(null);
        File.AppendAllText(Path.Combine(_game, Ysbin), "atualização do jogo");

        await vm.ReloadAsync();

        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.Contains(Ysbin, vm.PlanMessage);
        Assert.Empty(vm.Changes);
    }

    private sealed class LaunchRecorder : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public void Start(string executablePath, string workingDirectory) => Executable = executablePath;
    }
}
