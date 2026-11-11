using System.Text;
using VNForge.Core.Engines;

namespace VNForge.Engines.YuRis.Tests;

/// <summary>Patch do nome da fonte no executável (E5.5 · #65), sobre executáveis sintéticos: nenhum arquivo de jogo entra no repositório.</summary>
public sealed class YuRisFontPatchTests : IDisposable
{
    private const string Exe = "jogo.exe";
    private const int NameOffset = 0x300;

    /// <summary><c>ＭＳ ゴシック</c> em Shift-JIS, 13 bytes.</summary>
    private static readonly byte[] JapaneseName = [0x82, 0x6C, 0x82, 0x72, 0x20, 0x83, 0x53, 0x83, 0x56, 0x83, 0x62, 0x83, 0x4E];

    private static readonly byte[] Gothic = Encoding.ASCII.GetBytes("MS Gothic");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-patch-" + Guid.NewGuid().ToString("N"));

    public YuRisFontPatchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ExePath => Path.Combine(_root, Exe);

    private static YuRisFontPatch Patch(string executable = Exe) => new(executable);

    /// <summary>Executável de mentira: cabeçalho <c>MZ</c>, enchimento, o nome japonês com terminador e 3 zeros, e os outros nomes da lista.</summary>
    private static byte[] SyntheticExe(byte[]? name = null, byte[]? afterName = null, bool withHeader = true, int nameCount = 1)
    {
        var bytes = new byte[0x1000];
        Array.Fill(bytes, (byte)0xCC);
        if (withHeader)
        {
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
        }

        for (var i = 0; i < nameCount; i++)
        {
            var at = NameOffset + (i * 0x40);
            Array.Clear(bytes, at - 1, 0x21);
            (name ?? JapaneseName).CopyTo(bytes, at);
            (afterName ?? [0x00, 0x00, 0x00]).CopyTo(bytes, at + (name ?? JapaneseName).Length);
        }

        Encoding.Unicode.GetBytes("ＭＳ ゴシック").CopyTo(bytes, 0x800);
        return bytes;
    }

    private void Write(byte[] bytes) => File.WriteAllBytes(ExePath, bytes);

    [Fact]
    public void Descreve_o_patch_e_o_arquivo_que_altera()
    {
        var patch = Patch("sub/jogo.exe");

        Assert.Equal("exe-font", patch.Id);
        Assert.Contains("MS Gothic", patch.Description);
        Assert.Equal(["sub/jogo.exe"], patch.AffectedFiles);
    }

    [Fact]
    public void Aplicar_de_novo_nao_muda_nada()
    {
        Write(SyntheticExe());
        Patch().Apply(_root);
        var once = File.ReadAllBytes(ExePath);

        Assert.True(Patch().CanApply(_root, out var reason));
        Assert.Null(reason);
        Patch().Apply(_root);

        Assert.Equal(once, File.ReadAllBytes(ExePath));
    }

    [Fact]
    public void Executavel_sem_o_nome_e_recusado_e_o_apply_nao_grava()
    {
        var other = SyntheticExe(name: Encoding.ASCII.GetBytes("Arial"));
        Write(other);

        Assert.False(Patch().CanApply(_root, out var reason));
        Assert.Contains("ＭＳ ゴシック", reason);
        var erro = Assert.Throws<InvalidOperationException>(() => Patch().Apply(_root));
        Assert.Equal(reason, erro.Message);
        Assert.Equal(other, File.ReadAllBytes(ExePath));
    }

    [Fact]
    public void Nome_repetido_e_recusado_porque_nao_se_sabe_qual_e_o_das_falas()
    {
        Write(SyntheticExe(nameCount: 2));

        Assert.False(Patch().CanApply(_root, out var reason));
        Assert.Contains("2 vezes", reason);
    }

    [Fact]
    public void Arquivo_sem_assinatura_MZ_e_recusado()
    {
        Write(SyntheticExe(withHeader: false));

        Assert.False(Patch().CanApply(_root, out var reason));
        Assert.Contains("MZ", reason);
    }

    [Fact]
    public void Executavel_ausente_e_recusado_com_o_nome_do_arquivo()
    {
        Assert.False(Patch().CanApply(_root, out var reason));
        Assert.Contains(Exe, reason);
    }

    [Fact]
    public void Caminho_fora_da_pasta_do_jogo_e_recusado()
    {
        Assert.False(Patch("../jogo.exe").CanApply(_root, out var reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void Engine_YU_RIS_oferece_o_patch_para_o_executavel_do_projeto()
    {
        var provider = Assert.IsType<IGamePatchProvider>(new YuRisEngine(), exactMatch: false);

        var patch = Assert.Single(provider.CreatePatches("bin/jogo.exe"));

        Assert.Equal("exe-font", patch.Id);
        Assert.Equal(["bin/jogo.exe"], patch.AffectedFiles);
    }
}
