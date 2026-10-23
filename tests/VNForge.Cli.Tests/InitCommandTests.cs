using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Cli.Tests;

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

    [Fact]
    public async Task Saida_segue_o_exemplo_do_fluxo_tipico()
    {
        var game = NewGameDir();

        var result = await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game], TestContext.Current.CancellationToken);

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(
            [
                "Engine detectada: YU-RIS (YPF v500, YSTB v500)",
                "Arquivo de scripts: ysbin.ypf (2 scripts)",
                "Chave XOR dos scripts: 0xD36FAC96 (padrão)",
                "Projeto criado em minha-vn-ptbr" + Path.DirectorySeparatorChar,
            ],
            lines);
    }

    [Fact]
    public async Task Pasta_que_ja_tem_projeto_da_erro_sem_sobrescrever()
    {
        var game = NewGameDir();
        await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game, "--name", "Original"], TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(Path.Combine(ProjectPath(), "vnforge.json"), TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game, "--name", "Outro"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Já existe um vnforge.json", result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(ProjectPath(), "vnforge.json"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Jogo_sem_engine_reconhecida_da_erro_sem_criar_o_projeto()
    {
        var game = NewGameDir(withScripts: false);

        var result = await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Engine não reconhecida", result.Error);
        Assert.False(Directory.Exists(ProjectPath()));
    }

    [Fact]
    public async Task Nao_grava_nada_na_pasta_do_jogo()
    {
        var game = NewGameDir();
        var before = Directory.EnumerateFiles(game).Order().Select(f => (f, File.ReadAllBytes(f))).ToList();

        await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game], TestContext.Current.CancellationToken);

        var after = Directory.EnumerateFileSystemEntries(game).Order().ToList();
        Assert.Equal(before.Select(b => b.f), after);
        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Projeto_dentro_da_pasta_do_jogo_da_erro()
    {
        var game = NewGameDir();

        var result = await _cli.RunAsync(["init", Path.Combine(game, "traducao"), GameOption, game], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("não pode ficar dentro da pasta do jogo", result.Error);
        Assert.False(Directory.Exists(Path.Combine(game, "traducao")));
    }

    [Fact]
    public async Task Caminhos_relativos_partem_da_pasta_atual()
    {
        var game = NewGameDir();
        Directory.Move(game,Path.Combine(_cli.WorkingDirectory, "jogo"));

        var result = await _cli.RunAsync(["init", "proj", GameOption, "jogo"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(ProjectPath("proj"), "vnforge.json")));
    }

    [Fact]
    public async Task Json_traz_o_projeto_criado()
    {
        var game = NewGameDir();

        var result = await _cli.RunAsync(["init", MinhaVnPtbr, GameOption, game, "--json"], TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("yuris", data.GetProperty("engine").GetString());
        Assert.Equal(500, data.GetProperty("version").GetInt32());
        Assert.Equal("0xD36FAC96", data.GetProperty("scriptKey").GetString());
        Assert.Equal(ProjectPath(), data.GetProperty("folder").GetString());
    }

    [Fact]
    public async Task Sem_a_opcao_game_o_erro_de_uso_esta_em_portugues()
    {
        var result = await _cli.RunAsync(["init", MinhaVnPtbr], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("A opção '--game' é obrigatória.", result.Error);
    }

    [Fact]
    public async Task Help_do_init_esta_em_portugues()
    {
        var result = await _cli.RunAsync(["init", "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf init", result.Output);
        Assert.Contains("--game <dir>", result.Output);
        Assert.Contains("<pasta>", result.Output);
    }
}
