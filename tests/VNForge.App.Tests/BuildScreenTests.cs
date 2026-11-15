using System.Security.Cryptography;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Engines.YuRis;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.App.Tests;

/// <summary>#103: a tela Build e injeção faz o build numa pasta de jogo sintética, com o Core e a engine YU-RIS reais.</summary>
[Trait("Risco", "R6")]
public sealed class BuildScreenTests : IDisposable
{
    private const string Ysbin = "ysbin.ypf";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly TempFolder _root = new();
    private readonly string _game;
    private readonly string _projectPath;
    private readonly string _originalHash;

    public BuildScreenTests()
    {
        _game = _root.SubFolder("jogo");
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        File.WriteAllBytes(Path.Combine(_game, Ysbin), ScriptArchive());
        _originalHash = Hash(Path.Combine(_game, Ysbin));
        _projectPath = Path.Combine(_root.Path, "minha-vn-ptbr");
    }

    public void Dispose() => _root.Dispose();

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

    [Fact]
    public async Task Build_sem_traducao_gera_pacote_identico_e_nada_a_injetar()
    {
        IReadOnlyList<IEngine> engines = [new YuRisEngine()];
        ProjectInitializer.Initialize(_projectPath, _game, engines);
        ProjectExtractor.Extract(ProjectFolder.Open(_projectPath), engines, cancellationToken: TestContext.Current.CancellationToken);
        var vm = new BuildViewModel(new BackgroundTaskService(), engines, new LaunchRecorder(), new QaEngine([]))
        {
            Project = ProjectSummaryReader.Read(_projectPath),
        };
        await vm.ReloadAsync();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(_originalHash, Hash(Path.Combine(_projectPath, "build", Ysbin)));
        Assert.Empty(vm.Changes);
        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.Contains("já estão iguais", vm.PlanMessage);
    }

    private sealed class LaunchRecorder : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
