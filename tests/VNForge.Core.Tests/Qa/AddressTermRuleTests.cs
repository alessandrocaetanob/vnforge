using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class AddressTermRuleTests
{
    private static TextUnit Unit(string id, string source, string? translation, string? speaker, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation, status);

    private static QaContext Context(IReadOnlyList<AddressTerm> terms, params TextUnit[] units) =>
        new(units.GroupBy(u => u.Id.Split(':')[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)g.ToList()),
            [], [], FormattingProfile.Default)
        {
            AddressTerms = terms,
        };

    private static readonly AddressTerm[] Kenta = [new("健太", "美咲", "美咲ちゃん", "Misaki-chan")];

    [Fact]
    public void Nao_avisa_quando_o_original_nao_traz_o_tratamento_ou_o_falante_e_outro()
    {
        var context = Context(
            Kenta,
            Unit("a:1", "おはよう", "Bom dia", "健太"),
            Unit("a:2", "美咲ちゃん、おはよう", "Misaki, bom dia", "凛"),
            Unit("a:3", "美咲ちゃん、おはよう", "Misaki, bom dia", null));

        Assert.Empty(new AddressTermRule().Check(context));
    }

    [Fact]
    public void Linha_sem_traducao_e_par_incompleto_na_tabela_nao_avisam()
    {
        var terms = new AddressTerm[] { new("健太", "美咲", "美咲ちゃん", " "), new("健太", "凛", "", "Rin") };
        var context = Context(
            terms,
            Unit("a:1", "美咲ちゃん、凛", "Misaki, Rin", "健太"),
            Unit("a:2", "美咲ちゃん", null, "健太", TranslationStatus.Untranslated));

        Assert.Empty(new AddressTermRule().Check(context));
    }
}
