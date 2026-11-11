using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace VNForge.Patching.Tests;

/// <summary>A versão do jogo e a capa no manifesto, e os patches antigos, que não têm nenhum dos dois.</summary>
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
    public void O_manifesto_usa_nomes_em_camelCase_e_acentos_sem_escape()
    {
        using var game = new SyntheticGame();
        var bytes = game.BuildPackage(SyntheticGame.Info() with { ReadMe = "Instalação rápida", GameVersion = "1.00" }).ToArray();

        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
        var json = Encoding.UTF8.GetString(bytes, 16, length);

        Assert.Contains("\"gameName\":", json);
        Assert.Contains("\"gameVersion\":\"1.00\"", json);
        Assert.Contains("Instalação rápida", json);
    }

    [Fact]
    public void Capa_maior_que_o_limite_e_recusada_ao_montar_o_patch()
    {
        using var game = new SyntheticGame();
        using var stream = new MemoryStream();

        var error = Assert.Throws<PatchException>(() =>
            PatchBuilder.Build(SyntheticGame.Info() with { Cover = new byte[PatchPackage.MaxCoverBytes + 1] }, game.Sources(), stream, cancellationToken: Ct));

        Assert.Contains("capa", error.Message);
    }

    [Fact]
    public void Manifesto_da_instalacao_continua_gravando_e_lendo_pelo_gerador_de_JSON()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        PatchInstaller.Install(game.Game, package, null, Ct);

        var text = File.ReadAllText(Path.Combine(game.Game, PatchInstaller.BackupFolderName, "manifest.json"));
        Assert.Contains("\"installed\":true", text);
        Assert.Equal("1.2.0", PatchInstaller.InstalledVersion(game.Game));
    }
}
