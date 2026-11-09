namespace VNForge.Patching.Tests;

public class InstallerBundleTests
{
    [Fact]
    public void O_instalador_e_o_executavel_mais_o_patch_e_le_o_patch_de_volta()
    {
        using var game = new SyntheticGame();
        var stub = Path.Combine(game.Root, "stub.exe");
        var installer = Path.Combine(game.Root, "instalador.exe");
        File.WriteAllBytes(stub, SyntheticGame.Random(5, 50_000));

        using (var package = game.BuildPackage())
        {
            InstallerBundle.Create(stub, package, installer);
        }

        using var bundle = InstallerBundle.TryOpen(installer);
        Assert.NotNull(bundle);
        Assert.Equal("1.2.0", bundle.Manifest.Version);
        Assert.True(File.ReadAllBytes(installer).AsSpan(0, 50_000).SequenceEqual(File.ReadAllBytes(stub)));
        Assert.Equal(PatchCheckState.Ready, PatchInstaller.Check(game.Game, bundle).State);
    }

    [Fact]
    public void Executavel_sem_patch_nao_abre_como_instalador()
    {
        using var game = new SyntheticGame();
        var plain = Path.Combine(game.Root, "plain.exe");
        File.WriteAllBytes(plain, SyntheticGame.Random(6, 10_000));

        Assert.Null(InstallerBundle.TryOpen(plain));
    }
}
