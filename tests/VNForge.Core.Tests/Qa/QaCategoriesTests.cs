using VNForge.Core.Qa;

namespace VNForge.Core.Tests.Qa;

public sealed class QaCategoriesTests
{
    [Fact]
    public void Toda_regra_do_qa_padrao_tem_categoria_propria()
    {
        var uncategorized = QaEngine.Default.Rules.Where(r => QaCategories.Of(r.Id) == QaCategories.Other).Select(r => r.Id);

        Assert.Empty(uncategorized);
    }

    [Theory]
    [InlineData("marcador-diferente", "Marcadores")]
    [InlineData("caixa-acima-do-limite", "Caixa de texto")]
    [InlineData("tamanho-em-bytes", "Caixa de texto")]
    [InlineData("espaco-duplo", "Espaços")]
    [InlineData("aspas-desbalanceadas", "Pontuação")]
    [InlineData("caractere-nao-codificavel", "Acentuação")]
    [InlineData("glossario-diferente", "Glossário")]
    [InlineData("traducao-inconsistente", "Consistência")]
    [InlineData("nao-traduzida", "Tradução")]
    public void Regra_conhecida_cai_na_categoria_do_app(string ruleId, string category) =>
        Assert.Equal(category, QaCategories.Of(ruleId));

    [Fact]
    public void Regra_desconhecida_cai_em_outras()
    {
        Assert.Equal(QaCategories.Other, QaCategories.Of("regra-de-teste"));
    }

    [Fact]
    public void Lista_de_categorias_cobre_todas_as_da_tabela_e_termina_em_outras()
    {
        var used = QaEngine.Default.Rules.Select(r => QaCategories.Of(r.Id)).Distinct();

        Assert.All(used, category => Assert.Contains(category, QaCategories.All));
        Assert.Equal(QaCategories.Other, QaCategories.All[^1]);
    }
}
