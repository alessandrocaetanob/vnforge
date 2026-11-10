using Avalonia.Controls;
using Avalonia.Controls.Documents;
using VNForge.App.Controls;

namespace VNForge.App.Tests.Controls;

/// <summary>O texto da sugestão do editor (#532): no inspetor o começo igual fica sem marca e só o resto aparece riscado e depois proposto, como no quadro.</summary>
public sealed class SuggestionTextTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private Task<IReadOnlyList<(string Text, string? Style)>> Runs(string current, string proposed, bool showRemoved) => fixture.Session.Dispatch<IReadOnlyList<(string, string?)>>(
        () =>
        {
            var block = new TextBlock();
            SuggestionText.SetShowRemoved(block, showRemoved);
            SuggestionText.SetCurrent(block, current);
            SuggestionText.SetProposed(block, proposed);
            return block.Inlines!.OfType<Run>().Select(r => (r.Text ?? string.Empty, r.Classes.FirstOrDefault())).ToList();
        },
        TestContext.Current.CancellationToken);

    [Fact]
    public async Task No_inspetor_o_comeco_igual_fica_sem_marca_e_o_resto_aparece_riscado_e_proposto()
    {
        var runs = await Runs("Aizawa, Sakura. Os dois, cinco minutos atrasados.", "Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", showRemoved: true);

        Assert.Equal(
            [("Aizawa, Sakura. ", null), ("Os dois, cinco minutos atrasados.", "sug-del"), (" ", null), ("Vocês dois, cinco minutos atrasados.", "ins")],
            runs);
    }

    [Fact]
    public async Task O_comeco_igual_para_na_palavra_inteira()
    {
        var runs = await Runs("Os dois chegaram.", "Os doisinho chegaram.", showRemoved: true);

        Assert.Equal([("Os ", null), ("dois chegaram.", "sug-del"), (" ", null), ("doisinho chegaram.", "ins")], runs);
    }

    [Fact]
    public async Task Sem_comeco_igual_o_texto_de_hoje_vem_riscado_inteiro()
    {
        var runs = await Runs("Esqueceu o guarda-chuva?", "Você esqueceu o guarda-chuva?", showRemoved: true);

        Assert.Equal([("Esqueceu o guarda-chuva?", "sug-del"), (" ", null), ("Você esqueceu o guarda-chuva?", "ins")], runs);
    }

    [Fact]
    public async Task Proposta_igual_a_de_hoje_nao_risca_nada()
    {
        var runs = await Runs("Tudo certo.", "Tudo certo.", showRemoved: true);

        Assert.Equal([("Tudo certo.", null)], runs);
    }

    [Fact]
    public async Task No_Modo_revisao_so_a_proposta_aparece_com_as_palavras_novas_marcadas()
    {
        var runs = await Runs("Os dois chegaram cedo.", "Vocês dois chegaram cedo.", showRemoved: false);

        Assert.DoesNotContain(runs, r => r.Style == "sug-del");
        Assert.Contains(runs, r => r.Text == "Vocês" && r.Style == "ins");
    }

    [Theory]
    [InlineData(double.NaN, false, double.NaN)]
    [InlineData(double.NaN, true, 360d)]
    [InlineData(270d, true, 270d)]
    [InlineData(270d, false, 270d)]
    public void A_altura_do_palco_e_a_da_fila_ou_360_com_sugestao(double queue, bool hasSuggestion, double expected) =>
        Assert.Equal(expected, ReviewStage.HeightFor(queue, hasSuggestion));
}
