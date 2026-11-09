namespace VNForge.Vndb.Tests;

public class VndbTokenResolverTests
{
    [Fact]
    public void Le_a_variavel_VNDB_TOKEN_e_tira_os_espacos()
    {
        string? asked = null;

        var token = VndbTokenResolver.Resolve(name =>
        {
            asked = name;
            return "  abcd-efgh  ";
        });

        Assert.Equal("VNDB_TOKEN", asked);
        Assert.Equal("abcd-efgh", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Variavel_ausente_ou_vazia_nao_e_token(string? value)
    {
        Assert.Null(VndbTokenResolver.Resolve(_ => value));
    }
}
