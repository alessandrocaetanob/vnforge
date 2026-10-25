using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Engines.YuRis;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary>#109: o projeto que o assistente do app cria é igual ao do <c>vnf init</c> com as mesmas escolhas (jogo sintético, nunca arquivo de jogo).</summary>
public sealed class WizardEquivalenceTests : IDisposable
{
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public WizardEquivalenceTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        File.WriteAllBytes(Path.Combine(_game, "ysbin.ypf"), ScriptArchive());
    }

    public void Dispose() => _cli.Dispose();

    private string CliProject => Path.Combine(_cli.Root, "projeto-cli");

    private string AppProject => Path.Combine(_cli.Root, "projeto-app");

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

    private async Task<NewProjectWizardViewModel> RunWizardAsync(Action<NewProjectWizardViewModel>? choose = null)
    {
        var wizard = new NewProjectWizardViewModel(new NoPicker(), [new YuRisEngine()]);
        await wizard.SetGameFolderAsync(_game);
        wizard.SetProjectDirectory(AppProject);
        wizard.ProjectName = "Minha VN";
        choose?.Invoke(wizard);
        for (var i = 0; i < 4; i++)
        {
            await wizard.NextCommand.ExecuteAsync(null);
        }

        Assert.Equal(WizardStep.Done, wizard.Step);
        return wizard;
    }

    private async Task RunInitAsync(params string[] extra)
    {
        var result = await _cli.RunAsync(["init", CliProject, "--game", _game, "--name", "Minha VN", .. extra], TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(0, result.ExitCode);
    }

    private static Dictionary<string, string> Tree(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => File.Exists(path) ? File.ReadAllText(path) : "<pasta>");

    [Fact]
    public async Task Com_as_escolhas_padrao_o_projeto_e_igual_ao_do_vnf_init()
    {
        await RunInitAsync();
        await RunWizardAsync();

        Assert.Equal(Tree(CliProject), Tree(AppProject));
    }

    [Fact]
    public async Task Com_idiomas_acentuacao_e_modelo_escolhidos_o_projeto_e_igual_ao_do_vnf_init()
    {
        await RunInitAsync("--source", "en", "--target", "es", "--encoding", "cp1252", "--model", "claude-haiku-5-5");
        await RunWizardAsync(w =>
        {
            w.SourceLanguages.Select("en");
            w.TargetLanguages.Select("es");
            w.EncodingStrategies.Select("cp1252");
            w.Models.Select("claude-haiku-5-5");
        });

        Assert.Equal(Tree(CliProject), Tree(AppProject));
    }

    [Fact]
    public async Task Extrair_no_fim_do_assistente_grava_o_mesmo_texto_que_o_vnf_extract()
    {
        await RunInitAsync();
        var extract = await _cli.RunAsync(["extract", "--project", CliProject], TestContext.Current.CancellationToken);
        Assert.Equal(0, extract.ExitCode);
        var wizard = await RunWizardAsync();

        await wizard.ExtractCommand.ExecuteAsync(null);

        Assert.True(wizard.IsExtracted);
        Assert.Contains("1 scripts", wizard.ExtractionMessage);
        Assert.Equal(Text(CliProject), Text(AppProject));
    }

    [Fact]
    public async Task Opcoes_da_CLI_fora_do_vnforge_json_dao_erro_sem_criar_o_projeto()
    {
        var result = await _cli.RunAsync(["init", CliProject, "--game", _game, "--encoding", "inexistente"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(CliProject, "vnforge.json")));
    }

    private static Dictionary<string, string> Text(string project) =>
        Tree(Path.Combine(project, "text")).Where(e => e.Value != "<pasta>").ToDictionary();

    private sealed class NoPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }
}
