using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public class GameNamesTests
{
    [Theory]
    [InlineData("[Key] CLANNAD v1.02", "CLANNAD")]
    [InlineData("Steins;Gate (DL版)", "Steins;Gate")]
    [InlineData("Eden_Fall_ver2.1_Setup", "Eden Fall")]
    [InlineData("RJ01234567 Moonlit Tale", "Moonlit Tale")]
    [InlineData("Moonlit.Tale", "Moonlit Tale")]
    [InlineData("[Brand]", "Brand")]
    [InlineData("Sakura no Uta", "Sakura no Uta")]
    [InlineData("【Key】Little Busters！ 1.0.3", "Little Busters！")]
    public void Pasta_vira_titulo_sem_ruido_de_colecao(string name, string expected)
    {
        Assert.Equal(expected, GameNames.CleanFolderName(Path.Combine("jogos", name)));
        Assert.Equal(expected, GameNames.CleanFolderName(Path.Combine("jogos", name) + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
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
    [InlineData("CLANNAD.exe", "CLANNAD")]
    [InlineData("Sakura_no_Uta.exe", "Sakura no Uta")]
    [InlineData("game.exe", null)]
    [InlineData("Start.exe", null)]
    [InlineData(null, null)]
    public void Executavel_generico_nao_serve_de_termo(string? exe, string? expected)
    {
        Assert.Equal(expected, GameNames.CleanExeName(exe));
    }

    [Theory]
    [InlineData("  Steins;Gate  ", "Steins;Gate")]
    [InlineData("KiriKiri", null)]
    [InlineData("   ", null)]
    public void Titulo_da_janela_generico_ou_vazio_e_descartado(string? title, string? expected)
    {
        Assert.Equal(expected, GameNames.CleanWindowTitle(title));
    }

    [Fact]
    public void Termos_saem_na_ordem_janela_pasta_exe_sem_repetir()
    {
        var terms = GameNames.SearchTerms("CLANNAD", "CLANNAD.exe", Path.Combine("jogos", "[Key] CLANNAD v1.02"));

        Assert.Equal(
            [("CLANNAD", TermSource.WindowTitle), ("CLANNAD", TermSource.Folder), ("CLANNAD", TermSource.Exe)],
            terms.Select(t => (t.Text, t.Source)));
    }

    [Fact]
    public void Termo_curto_ou_so_de_numeros_nao_entra()
    {
        Assert.Empty(GameNames.SearchTerms(null, "x.exe", Path.Combine("jogos", "2024")));
    }
}
