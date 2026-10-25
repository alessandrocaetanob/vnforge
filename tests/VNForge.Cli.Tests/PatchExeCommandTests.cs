using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

/// <summary><c>vnf patch-exe</c>: troca o nome da fonte no executável pelo fluxo de backup e restauração da injeção (E5.5 · #65).</summary>
public sealed class PatchExeCommandTests : IDisposable
{
    private const string PatchExe = "patch-exe";
    private const string Restore = "restore";
    private const string YesOption = "--yes";
    private const string Exe = "jogo.exe";
    private const int NameOffset = 0x300;

    private static readonly byte[] JapaneseName = [0x82, 0x6C, 0x82, 0x72, 0x20, 0x83, 0x53, 0x83, 0x56, 0x83, 0x62, 0x83, 0x4E];

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly ProjectFolder _project;

    public PatchExeCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        _project = ProjectFolder.Create(
            Path.Combine(_cli.Root, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, Exe),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private string ExePath => Path.Combine(_game, Exe);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] SyntheticExe()
    {
        var bytes = new byte[0x1000];
        Array.Fill(bytes, (byte)0xCC);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        Array.Clear(bytes, NameOffset, 0x20);
        JapaneseName.CopyTo(bytes, NameOffset);
        return bytes;
    }

    private byte[] PutExe(byte[]? content = null)
    {
        var bytes = content ?? SyntheticExe();
        File.WriteAllBytes(ExePath, bytes);
        return bytes;
    }

    private Task<RunResult> Run(params string[] args) => _cli.RunAsync([.. args, "--project", _project.Root], Token);

    private string ExeSha256() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ExePath)));

    [Fact]
    public async Task Com_yes_troca_o_nome_da_fonte_e_faz_backup_do_executavel_original()
    {
        var original = PutExe();

        var result = await Run(PatchExe, YesOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("Backup:", result.Output);
        Assert.Contains("hashes conferidos", result.Output);
        var after = await File.ReadAllBytesAsync(ExePath, Token);
        Assert.Equal(Encoding.ASCII.GetBytes("MS Gothic"), after[NameOffset..(NameOffset + 9)]);
        Assert.Equal(13, Enumerable.Range(0, original.Length).Count(i => original[i] != after[i]));
        var backup = Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(backup, Exe), Token));
    }

    [Fact]
    public async Task Restore_devolve_o_executavel_original_byte_a_byte()
    {
        PutExe();
        var originalHash = ExeSha256();
        await Run(PatchExe, YesOption);
        Assert.NotEqual(originalHash, ExeSha256());

        var result = await Run(Restore, YesOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(originalHash, ExeSha256());
    }

    [Fact]
    public async Task Executavel_que_nao_e_o_esperado_e_recusado_sem_backup_e_sem_gravar()
    {
        var other = SyntheticExe();
        Array.Clear(other, NameOffset, JapaneseName.Length);
        PutExe(other);

        var result = await Run(PatchExe, YesOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("recusado", result.Error);
        Assert.Contains("ＭＳ ゴシック", result.Error);
        Assert.Equal(other, await File.ReadAllBytesAsync(ExePath, Token));
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Executavel_ausente_e_recusado()
    {
        var result = await Run(PatchExe, YesOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Exe, result.Error);
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Recusar_a_confirmacao_sai_com_3_sem_gravar_nada()
    {
        var original = PutExe();
        _cli.SetInput("n\n");

        var result = await Run(PatchExe);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Operação cancelada", result.Output);
        Assert.Contains("[s/N]", result.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(ExePath, Token));
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
        Assert.False(Directory.Exists(Path.Combine(_project.CacheDirectory, "patch")) && Directory.EnumerateFileSystemEntries(Path.Combine(_project.CacheDirectory, "patch")).Any());
    }

    [Fact]
    public async Task Confirmar_com_s_aplica_o_patch()
    {
        PutExe();
        _cli.SetInput("s\n");

        var result = await Run(PatchExe);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Sem_yes_e_sem_entrada_pede_o_yes()
    {
        var original = PutExe();

        var result = await Run(PatchExe);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--yes", result.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(ExePath, Token));
    }

    [Fact]
    public async Task Executavel_ja_alterado_nao_gera_outro_backup()
    {
        PutExe();
        await Run(PatchExe, YesOption);
        var patchedHash = ExeSha256();

        var result = await Run(PatchExe, YesOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("já está", result.Output);
        Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal(patchedHash, ExeSha256());
    }

    [Fact]
    public async Task Em_terminal_interativo_mostra_a_tabela_do_que_vai_mudar_antes_de_perguntar()
    {
        PutExe();
        using var rich = new CliHarness(stdin: "n\n", terminal: TestTerminals.InteractivePlain(200));
        var project = ProjectFolder.Create(
            Path.Combine(rich.Root, "projeto"),
            _project.LoadConfig() with { Game = new GameConfig(_game, Exe) });

        var result = await rich.RunAsync([PatchExe, "--project", project.Root], Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("O que acontece", result.Output);
        Assert.Contains(Exe, result.Output);
        Assert.Contains("substitui o original", result.Output);
    }

    [Fact]
    public async Task Json_devolve_o_envelope_com_o_patch_e_o_backup()
    {
        PutExe();

        var result = await Run(PatchExe, YesOption, "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(_game, data.GetProperty("gameDirectory").GetString());
        var patch = Assert.Single(data.GetProperty("patches").EnumerateArray());
        Assert.Equal("exe-font", patch.GetProperty("id").GetString());
        Assert.Equal([Exe], patch.GetProperty("written").EnumerateArray().Select(e => e.GetString()));
        Assert.True(Directory.Exists(patch.GetProperty("backupDirectory").GetString()));
    }

    [Fact]
    public async Task Json_sem_yes_pede_o_yes_e_nao_grava()
    {
        var original = PutExe();

        var result = await Run(PatchExe, "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(ExePath, Token));
    }

    [Fact]
    public async Task Patch_exe_aparece_na_ajuda_como_experimental()
    {
        var result = await _cli.RunAsync([PatchExe, "--help"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Experimental", result.Output);
        Assert.Contains("vnf patch-exe", result.Output);
    }
}
