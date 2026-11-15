using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R2")]
public sealed class InitCommandTests : IDisposable
{
    private const string MinhaVnPtbr = "minha-vn-ptbr";
    private const string GameOption = "--game";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static byte[] Script() =>
        new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290).AddWord("こんにちは", 1).AddReturnCode(0, 2).Build().Bytes;

    private string NewGameDir(bool withScripts = true)
    {
        var dir = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "MinhaVN.exe"), "MZ");
        if (withScripts)
        {
            var builder = new SyntheticYpfBuilder(500)
                .AddFile("ysc.ybn", Yscm.Bytes)
                .AddFile("yst00001.ybn", Script())
                .AddFile("yst00002.ybn", Script());
            File.WriteAllBytes(Path.Combine(dir, "ysbin.ypf"), builder.Build().Bytes);
        }

        return dir;
    }

    private string ProjectPath(string name = MinhaVnPtbr) => Path.Combine(_cli.WorkingDirectory, name);

    [Fact]
    public async Task Cria_o_projeto_com_engine_versao_e_chave_da_deteccao()
    {
        var game = NewGameDir();

        var result = await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var config = ProjectFolder.Open(ProjectPath()).LoadConfig();
        Assert.Equal(new EngineConfig("yuris", 500, "0xD36FAC96"), config.Engine);
        Assert.Equal(MinhaVnPtbr, config.Name);
        Assert.Equal("MinhaVN.exe", config.Game.Exe);
        Assert.Equal(game.Replace(Path.DirectorySeparatorChar, '/'), config.Game.Path);
    }
}
