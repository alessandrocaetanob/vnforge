using System.Text.Json;

namespace VNForge.Cli.Tests;

public sealed class DetectCommandTests : IDisposable
{
    private const string Detect = "detect";
    private const string GameOption = "--game";
    private const string BgmYpf = "bgm.ypf";

    // YPF v500 vazio: assinatura, versão, 0 arquivos, header_size = 32 e 16 bytes de padding.
    private static readonly byte[] EmptyYpf = Convert.FromHexString(
        "59504600" + "F4010000" + "00000000" + "20000000" + "00000000000000000000000000000000");

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string NewGameDir()
    {
        var dir = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Pasta_sem_yuris_diz_que_a_engine_nao_foi_reconhecida()
    {
        var dir = NewGameDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "jogo.exe"), "MZ", TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync([Detect, GameOption, dir], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Engine não reconhecida", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task Pasta_com_ypf_mostra_a_engine_e_os_detalhes()
    {
        var dir = NewGameDir();
        await File.WriteAllBytesAsync(Path.Combine(dir, BgmYpf), EmptyYpf, TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync([Detect, GameOption, dir], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Engine detectada: yuris", result.Output);
        Assert.Contains("parece YU-RIS", result.Output);
        var lines = result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("Engine detectada: yuris", lines[0]);
        Assert.StartsWith("├── parece YU-RIS", lines[1]);
        var packages = Array.FindIndex(lines, l => l.EndsWith("Pacotes", StringComparison.Ordinal));
        Assert.True(packages > 1, "o nó Pacotes tem de vir depois do resumo");
        Assert.Matches(@"^[│ ]   └── bgm\.ypf: v500", lines[packages + 1]);
    }

    [Fact]
    public async Task Ypf_com_indice_ilegivel_mostra_os_avisos_com_rotulo_em_portugues()
    {
        var dir = NewGameDir();
        var broken = (byte[])EmptyYpf.Clone();
        broken[8] = 1;
        await File.WriteAllBytesAsync(Path.Combine(dir, BgmYpf), broken, TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync([Detect, GameOption, dir], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Engine detectada: yuris", result.Output);
        Assert.Contains("└── bgm.ypf: assinatura YPF, índice ilegível", result.Output);
        Assert.Contains("Avisos: bgm.ypf:", result.Output);
    }

    [Fact]
    public async Task Ypf_bloqueado_sem_outra_pista_mostra_o_aviso_e_nao_reconhece()
    {
        var dir = NewGameDir();
        var path = Path.Combine(dir, BgmYpf);
        await File.WriteAllBytesAsync(path, EmptyYpf, TestContext.Current.CancellationToken);
        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await _cli.RunAsync([Detect, GameOption, dir], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Engine não reconhecida", result.Output);
        Assert.Contains("Avisos: bgm.ypf: não foi possível abrir", result.Output);
    }

    [Fact]
    public async Task Sem_game_usa_a_pasta_atual()
    {
        await File.WriteAllBytesAsync(Path.Combine(_cli.WorkingDirectory, BgmYpf), EmptyYpf, TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync([Detect], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Engine detectada: yuris", result.Output);
    }

    [Fact]
    public async Task Json_traz_o_resultado_da_deteccao()
    {
        var dir = NewGameDir();
        await File.WriteAllBytesAsync(Path.Combine(dir, BgmYpf), EmptyYpf, TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync([Detect, GameOption, dir, "--json"], TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("detected").GetBoolean());
        Assert.Equal("yuris", data.GetProperty("engine").GetString());
        Assert.Equal("v500, 0 arquivos", data.GetProperty("details").GetProperty("ypf:bgm.ypf").GetString());
    }

    [Fact]
    public async Task Json_de_pasta_sem_engine_marca_detected_falso()
    {
        var dir = NewGameDir();

        var result = await _cli.RunAsync([Detect, GameOption, dir, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("detected").GetBoolean());
        Assert.False(data.TryGetProperty("engine", out _));
    }

    [Fact]
    public async Task Pasta_inexistente_e_erro_de_uso_em_portugues()
    {
        var result = await _cli.RunAsync([Detect, GameOption, Path.Combine(_cli.Root, "nao-existe")], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("A pasta do jogo não existe", result.Error);
    }

    [Fact]
    public async Task Detect_nao_grava_nada_na_pasta_do_jogo()
    {
        var dir = NewGameDir();
        await File.WriteAllBytesAsync(Path.Combine(dir, BgmYpf), EmptyYpf, TestContext.Current.CancellationToken);

        await _cli.RunAsync([Detect, GameOption, dir], TestContext.Current.CancellationToken);

        Assert.Equal([BgmYpf], Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToArray());
        Assert.Equal(EmptyYpf, await File.ReadAllBytesAsync(Path.Combine(dir, BgmYpf), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Help_do_detect_esta_em_portugues()
    {
        var result = await _cli.RunAsync([Detect, "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf detect", result.Output);
        Assert.Contains("--game <dir>", result.Output);
    }
}
