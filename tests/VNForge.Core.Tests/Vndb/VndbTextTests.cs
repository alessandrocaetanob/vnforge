using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

public sealed class VndbTextTests
{
    [Theory]
    [InlineData("[spoiler]só spoiler[/spoiler]")]
    public void Clean_devolve_nulo_quando_nao_sobra_texto(string? description) =>
        Assert.Null(VndbText.Clean(description));

    [Fact]
    public void Clean_tira_a_marcacao_e_deixa_o_texto_dos_links()
    {
        var text = VndbText.Clean("Uma [b]história[/b] de [i]exemplo[/i], veja [url=https://vndb.org/v17]o jogo[/url] e [url]https://x.org[/url].");

        Assert.Equal("Uma história de exemplo, veja o jogo e https://x.org.", text);
    }

    [Fact]
    public void Clean_tira_os_spoilers_inteiros_inclusive_de_varias_linhas()
    {
        var text = VndbText.Clean("Começo.\n[spoiler]O vilão\nera o irmão.[/SPOILER]\nFim.");

        Assert.Equal("Começo.\n\nFim.", text);
    }

    [Fact]
    public void Clean_junta_linhas_em_branco_repetidas_e_normaliza_a_quebra_de_linha()
    {
        Assert.Equal("a\n\nb", VndbText.Clean("a\r\n\r\n\r\n\r\nb  "));
    }
}
