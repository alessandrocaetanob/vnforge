using VNForge.Core.Engines;

namespace VNForge.Core.Tests;

[Trait("Risco", "R1")]
public class GamePathsTests
{
    private static readonly string GameDir = Path.Combine(Path.GetTempPath(), "jogo");

    [Theory]
    [InlineData("")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("/foo")]
    [InlineData("C:\\foo")]
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
