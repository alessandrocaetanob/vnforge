using VNForge.Core.Engines;

namespace VNForge.Core.Tests;

public class GamePathsTests
{
    private static readonly string GameDir = Path.Combine(Path.GetTempPath(), "jogo");

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
    public void Resolve_recusa_pasta_do_jogo_vazia()
    {
        Assert.Throws<ArgumentException>(() => GamePaths.Resolve("", "game.exe"));
    }
}
