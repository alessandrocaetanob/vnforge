using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public class GameNamesTests
{
    [Theory]
    [InlineData("Steins;Gate (DL版)", "Steins;Gate")]
    [InlineData("Eden_Fall_ver2.1_Setup", "Eden Fall")]
    [InlineData("RJ01234567 Moonlit Tale", "Moonlit Tale")]
    [InlineData("[Brand]", "Brand")]
    [InlineData("【Key】Little Busters！ 1.0.3", "Little Busters！")]
    public void Pasta_vira_titulo_sem_ruido_de_colecao(string name, string expected)
    {
        Assert.Equal(expected, GameNames.CleanFolderName(Path.Combine("jogos", name)));
        Assert.Equal(expected, GameNames.CleanFolderName(Path.Combine("jogos", name) + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.02")]
    public void Pasta_sem_nome_util_nao_gera_titulo(string? name)
    {
        Assert.Null(GameNames.CleanFolderName(string.IsNullOrEmpty(name) ? name : Path.Combine("jogos", name)));
    }

    [Fact]
    public void Raiz_de_unidade_nao_tem_titulo()
    {
        Assert.Null(GameNames.CleanFolderName(Path.GetPathRoot(Path.GetFullPath("."))));
    }

    [Theory]
    [InlineData("game.exe", null)]
    public void Executavel_generico_nao_serve_de_termo(string? exe, string? expected)
    {
        Assert.Equal(expected, GameNames.CleanExeName(exe));
    }

    [Fact]
    public void Termo_curto_ou_so_de_numeros_nao_entra()
    {
        Assert.Empty(GameNames.SearchTerms(null, "x.exe", Path.Combine("jogos", "2024")));
    }
}
