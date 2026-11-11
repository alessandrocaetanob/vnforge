using VNForge.Patching;
using VNForge.Patching.Tests;

namespace VNForge.Installer.Tests;

public class InstallerTextTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(2048, "2,0 KB")]
    [InlineData(5_347_737, "5,1 MB")]
    [InlineData(1_288_490_189, "1,2 GB")]
    public void Tamanhos_usam_virgula_e_unidade(long bytes, string expected) => Assert.Equal(expected, InstallerText.Size(bytes));

    [Theory]
    [InlineData(200, "0,2 s")]
    [InlineData(12_400, "12 s")]
    [InlineData(65_000, "1 min 5 s")]
    public void Tempos_seguem_o_formato_do_quadro(int milliseconds, string expected) => Assert.Equal(expected, InstallerText.Elapsed(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void Lista_de_nomes_une_com_e_e_resume_as_longas()
    {
        Assert.Equal("script.ypf", InstallerText.NameList(["script.ypf"]));
        Assert.Equal("script.ypf e Game.exe", InstallerText.NameList(["script.ypf", "Game.exe"]));
        Assert.Equal("script.ypf, Game.exe, font.ypf e system.ypf", InstallerText.NameList(["script.ypf", "Game.exe", "font.ypf", "system.ypf"]));
        Assert.Equal("a, b, c e mais 3 arquivos", InstallerText.NameList(["a", "b", "c", "d", "e", "f"]));
    }

    [Fact]
    public void O_hash_curto_leva_quatro_caracteres_de_cada_ponta()
    {
        Assert.Equal("3f9a…c21d", InstallerText.ShortHash("3f9a" + new string('0', 56) + "c21d"));
        Assert.Equal("(nenhum)", InstallerText.ShortHash(null));
    }

    [Theory]
    [InlineData(10L * 1024 * 1024, "Leva poucos segundos.")]
    [InlineData(4L * 1024 * 1024 * 1024, "Leva cerca de 3 minutos.")]
    public void A_estimativa_vira_uma_frase(long bytes, string expected) => Assert.Equal(expected, InstallerText.EstimateSentence(bytes));

    [Fact]
    public void O_executavel_do_jogo_e_o_exe_que_o_patch_troca_ou_o_unico_da_pasta()
    {
        using var game = new SyntheticGame();

        Assert.Equal(Path.Combine(game.Game, "Game", "Game.exe"), GameExecutable.Find(game.Game, Manifest("Game/Game.exe")));

        var alone = Directory.CreateDirectory(Path.Combine(game.Root, "so-um")).FullName;
        File.WriteAllBytes(Path.Combine(alone, "jogo.exe"), [1]);
        File.WriteAllBytes(Path.Combine(alone, "unins000.exe"), [1]);
        Assert.Equal(Path.Combine(alone, "jogo.exe"), GameExecutable.Find(alone, Manifest()));

        File.WriteAllBytes(Path.Combine(alone, "config.exe"), [1]);
        Assert.Null(GameExecutable.Find(alone, Manifest()));
        Assert.Null(GameExecutable.Find(Path.Combine(game.Root, "nao-existe"), Manifest()));
    }

    private static PatchManifest Manifest(params string[] paths) =>
        new("Jogo", "1.0.0", null, null, null, [.. paths.Select(p => new PatchFileEntry(p, new string('a', 64), 1, new string('b', 64), 1, 0, 1))]);
}
