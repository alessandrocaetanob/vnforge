using VNForge.Core.Engines;

namespace VNForge.Core.Tests;

public class GamePathsTests
{
    private static readonly string GameDir = Path.Combine(Path.GetTempPath(), "jogo");

    [Theory]
    [InlineData("")]
    [InlineData("/foo")]
    public void Resolve_recusa_caminho_invalido_ou_que_sai_da_pasta(string relativo)
    {
        Assert.Throws<ArgumentException>(() => GamePaths.Resolve(GameDir, relativo));
    }
}
