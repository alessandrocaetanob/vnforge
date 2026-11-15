using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace VNForge.Patching.Tests;

/// <summary>A versão do jogo e a capa no manifesto, e os patches antigos, que não têm nenhum dos dois.</summary>
[Trait("Risco", "R3")]
public class PatchManifestFieldsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Cover = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

    [Fact]
    public void A_versao_do_jogo_e_a_capa_vao_no_manifesto_e_voltam_iguais()
    {
        using var game = new SyntheticGame();
        using var bytes = game.BuildPackage(SyntheticGame.Info() with { GameVersion = "1.00", Cover = Cover });

        using var package = PatchPackage.Open(bytes);

        Assert.Equal("1.00", package.Manifest.GameVersion);
        Assert.Equal(Cover, package.Manifest.Cover);
    }

    [Fact]
    public void Patch_antigo_sem_versao_do_jogo_nem_capa_continua_abrindo()
    {
        using var game = new SyntheticGame();
        var current = game.BuildPackage(SyntheticGame.Info() with { GameVersion = "1.00", Cover = Cover }).ToArray();
        var length = BinaryPrimitives.ReadInt32LittleEndian(current.AsSpan(12, 4));
        var manifest = JsonNode.Parse(current.AsSpan(16, length))!.AsObject();
        manifest.Remove("gameVersion");
        manifest.Remove("cover");
        var oldJson = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var old = new MemoryStream();
        old.Write(current, 0, 12);
        old.Write(BitConverter.GetBytes(oldJson.Length));
        old.Write(oldJson);
        old.Write(current, 16 + length, current.Length - 16 - length);
        old.Position = 0;

        using var package = PatchPackage.Open(old);

        Assert.Null(package.Manifest.GameVersion);
        Assert.Null(package.Manifest.Cover);
        Assert.Equal("1.2.0", package.Manifest.Version);
        Assert.Equal(PatchCheckState.Ready, PatchInstaller.Check(game.Game, package).State);
    }
}
