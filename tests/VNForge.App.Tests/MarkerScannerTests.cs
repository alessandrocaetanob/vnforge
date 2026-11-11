using VNForge.App.Controls;

namespace VNForge.App.Tests;

public class MarkerScannerTests
{
    [Fact]
    public void Acha_posicao_e_tipo_dos_marcadores_de_controle_e_das_variaveis()
    {
        var spans = MarkerScanner.Scan(@"Oi\n{VAR:nome}, tudo bem?\p");

        Assert.Equal(
            [
                new MarkerSpan(2, 2, MarkerKind.Control),
                new MarkerSpan(4, 10, MarkerKind.Variable),
                new MarkerSpan(25, 2, MarkerKind.Control),
            ],
            spans);
    }

    [Theory]
    [InlineData("sem marcadores")]
    [InlineData(@"barra solta \")]
    [InlineData(@"desconhecido \x")]
    [InlineData("chave sem fechamento {VAR:a")]
    [InlineData("vazio {}")]
    [InlineData("")]
    public void Texto_incompleto_ou_invalido_fica_como_texto_comum(string texto) =>
        Assert.Empty(MarkerScanner.Scan(texto));
}