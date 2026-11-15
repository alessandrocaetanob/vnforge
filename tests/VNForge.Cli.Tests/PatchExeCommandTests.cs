using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

/// <summary><c>vnf patch-exe</c>: troca o nome da fonte no executável pelo fluxo de backup e restauração da injeção (E5.5 · #65).</summary>
[Trait("Risco", "R1")]
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
        Array.Clear(bytes, NameOffset - 1, 0x21);
        JapaneseName.CopyTo(bytes, NameOffset);
        Encoding.Unicode.GetBytes("ＭＳ ゴシック").CopyTo(bytes, 0x800);
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
}
