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
    public void Avisa_da_fala_que_traz_o_tratamento_no_original_e_perde_o_combinado()
    {
        var context = Context(Kenta, Unit("a:1", "美咲ちゃん、おはよう", "Misaki, bom dia", "健太"));

        var issue = Assert.Single(new AddressTermRule().Check(context));

        Assert.Equal((AddressTermRule.RuleId, QaSeverity.Warning, "a:1", "a"), (issue.RuleId, issue.Severity, issue.UnitId, issue.ScriptKey));
        Assert.Contains("健太", issue.Message);
        Assert.Contains("美咲ちゃん", issue.Message);
        Assert.Contains("Misaki-chan", issue.Message);
    }

    [Fact]
    public void Nao_avisa_quando_a_traducao_traz_o_combinado_mesmo_em_outra_caixa()
    {
        var context = Context(Kenta, Unit("a:1", "美咲ちゃん、おはよう", "Bom dia, MISAKI-CHAN!", "健太"));

        Assert.Empty(new AddressTermRule().Check(context));
    }

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

    [Fact]
    public void O_mesmo_tratamento_para_dois_pares_aceita_qualquer_um_dos_combinados()
    {
        var terms = new AddressTerm[] { new("凛", "健太", "先輩", "senpai"), new("凛", "拓也", "先輩", "veterano") };
        var context = Context(
            terms,
            Unit("a:1", "先輩、こっち", "Veterano, aqui", "凛"),
            Unit("a:2", "先輩、こっち", "Ei, aqui", "凛"));

        var issue = Assert.Single(new AddressTermRule().Check(context));

        Assert.Equal("a:2", issue.UnitId);
        Assert.Contains("senpai", issue.Message);
        Assert.Contains("veterano", issue.Message);
    }

    [Fact]
    public void O_falante_trocado_na_linha_vale_no_lugar_do_original()
    {
        var unit = Unit("a:1", "美咲ちゃん", "Misaki", "凛") with { ChosenSpeaker = "健太" };

        Assert.Single(new AddressTermRule().Check(Context(Kenta, unit)));
    }

    [Fact]
    public void Sem_tratamentos_no_projeto_nada_a_conferir()
    {
        Assert.Empty(new AddressTermRule().Check(Context([], Unit("a:1", "美咲ちゃん", "Misaki", "健太"))));
    }

    [Fact]
    public void A_regra_e_aviso_de_estilo_na_categoria_Estilo_e_nao_trava_o_build()
    {
        Assert.NotNull(QaEngine.Default.Find(AddressTermRule.RuleId));
        Assert.Equal(QaCategories.Style, QaCategories.Of(AddressTermRule.RuleId));

        var advice = new QaIssue(AddressTermRule.RuleId, QaSeverity.Warning, "a:1", "a", "x");
        var spaces = new QaIssue("espaco-duplo", QaSeverity.Warning, "a:2", "a", "z");

        Assert.False(new QaReport([advice], [], []).Blocks(allowWarnings: false));
        Assert.Equal(1, new QaReport([advice, spaces], [], []).StyleAdviceCount);
        Assert.Equal([spaces], new QaReport([advice, spaces], [], []).BlockingIssues);
    }

    [Fact]
    public void O_motor_silencia_o_aviso_marcado_como_ok_na_linha()
    {
        var unit = Unit("a:1", "美咲ちゃん", "Misaki", "健太") with { Flags = [AddressTermRule.RuleId] };

        var report = new QaEngine([new AddressTermRule()]).Run(Context(Kenta, unit));

        Assert.Empty(report.Issues);
        Assert.Single(report.Silenced);
    }
}
