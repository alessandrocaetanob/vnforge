using VNForge.Core.Engines;

namespace VNForge.Core.Tests;

public class GamePathsTests
{
    private static readonly string GameDir = Path.Combine(Path.GetTempPath(), "jogo");

    [Theory]
    [InlineData("game.exe", "game.exe")]
    [InlineData("data/ysbin.ypf", "data/ysbin.ypf")]
    [InlineData("data\\ysbin.ypf", "data/ysbin.ypf")]
    [InlineData("./game.exe", "game.exe")]
    [InlineData("a/../game.exe", "game.exe")]
    public void Resolve_devolve_caminho_dentro_da_pasta(string relativo, string esperado)
    {
        var resolvido = GamePaths.Resolve(GameDir, relativo);

        Assert.Equal(Path.GetFullPath(Path.Combine(GameDir, esperado)), resolvido);
    }

    [Fact]
    public void Resolve_aceita_pasta_com_separador_final()
    {
        var resolvido = GamePaths.Resolve(GameDir + Path.DirectorySeparatorChar, "game.exe");

        Assert.Equal(Path.GetFullPath(Path.Combine(GameDir, "game.exe")), resolvido);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("a/../../x")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("/foo")]
    [InlineData("\\foo")]
    [InlineData("\\\\servidor\\share\\x")]
    [InlineData("//servidor/share/x")]
    [InlineData("C:foo")]
    [InlineData("C:\\foo")]
    [InlineData("C:/foo")]
    [InlineData("game.exe:stream")]
    [InlineData(":stream")]
    public void Resolve_recusa_caminho_invalido_ou_que_sai_da_pasta(string relativo)
    {
        Assert.Throws<ArgumentException>(() => GamePaths.Resolve(GameDir, relativo));
    }

    [Fact]
    public void Resolve_nao_confunde_pasta_irma_com_prefixo_igual()
    {
        var irma = Path.GetFileName(GameDir) + "X";

        Assert.Throws<ArgumentException>(() => GamePaths.Resolve(GameDir, "../" + irma + "/game.exe"));
    }

    [Fact]
    public void Resolve_recusa_pasta_do_jogo_vazia()
    {
        Assert.Throws<ArgumentException>(() => GamePaths.Resolve("", "game.exe"));
    }
}
