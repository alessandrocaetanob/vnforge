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
    [InlineData(@"\n", 1)]
    [InlineData(@"\p", 1)]
    [InlineData(@"\c", 1)]
    [InlineData(@"\u", 1)]
    [InlineData(@"\\", 1)]
    [InlineData(@"\{", 1)]
    [InlineData(@"\n\p\c", 3)]
    public void Reconhece_a_gramatica_do_ControlMarkers(string texto, int esperados) =>
        Assert.Equal(esperados, MarkerScanner.Scan(texto).Count);

    [Theory]
    [InlineData("sem marcadores")]
    [InlineData(@"barra solta \")]
    [InlineData(@"desconhecido \x")]
    [InlineData("chave sem fechamento {VAR:a")]
    [InlineData("vazio {}")]
    [InlineData("")]
    public void Texto_incompleto_ou_invalido_fica_como_texto_comum(string texto) =>
        Assert.Empty(MarkerScanner.Scan(texto));

    [Fact]
    public void Barra_escapada_nao_vira_inicio_de_outro_marcador()
    {
        var spans = MarkerScanner.Scan(@"\\n");

        Assert.Equal([new MarkerSpan(0, 2, MarkerKind.Control)], spans);
    }

    [Fact]
    public void Chave_escapada_nao_abre_variavel()
    {
        var spans = MarkerScanner.Scan(@"\{VAR:a}");

        Assert.Equal([new MarkerSpan(0, 2, MarkerKind.Control)], spans);
    }

    [Fact]
    public void FindFrom_devolve_o_primeiro_marcador_a_partir_da_posicao()
    {
        const string Texto = @"a\nb\pc";

        Assert.Equal(new MarkerSpan(1, 2, MarkerKind.Control), MarkerScanner.FindFrom(Texto, 0));
        Assert.Equal(new MarkerSpan(1, 2, MarkerKind.Control), MarkerScanner.FindFrom(Texto, 1));
        Assert.Equal(new MarkerSpan(4, 2, MarkerKind.Control), MarkerScanner.FindFrom(Texto, 2));
        Assert.Null(MarkerScanner.FindFrom(Texto, 5));
    }

    [Fact]
    public void End_e_o_fim_exclusivo() =>
        Assert.Equal(12, new MarkerSpan(2, 10, MarkerKind.Variable).End);

    [Fact]
    public void Texto_nulo_e_recusado() =>
        Assert.Throws<ArgumentNullException>(() => MarkerScanner.Scan(null!));

    [Theory]
    [InlineData(@"\n", MarkerKind.Control, "quebra de linha")]
    [InlineData(@"\p", MarkerKind.Control, "nova página")]
    [InlineData(@"\c", MarkerKind.Control, "esperar clique")]
    [InlineData(@"\u", MarkerKind.Control, "desconhecido")]
    [InlineData(@"\\", MarkerKind.Control, "barra invertida")]
    [InlineData(@"\{", MarkerKind.Control, "chave literal")]
    [InlineData("{VAR:nome}", MarkerKind.Variable, "Variável")]
    [InlineData(@"\z", MarkerKind.Control, @"\z")]
    public void Nome_acessivel_descreve_o_marcador(string marcador, MarkerKind tipo, string trecho) =>
        Assert.Contains(trecho, MarkerLabels.Describe(marcador, tipo));
}
