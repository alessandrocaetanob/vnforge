using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
public class ReplacementTests
{
    private static FormattingProfile ProfileWith(ReplacementRules rules) => FormattingProfile.Default with { Replacements = rules };

    [Fact]
    public void Create_registra_as_duas_fases_a_partir_do_perfil()
    {
        var profile = ProfileWith(new ReplacementRules(
            [new Replacement("Tokyo", "Tóquio")],
            [new Replacement(@"\s+$", string.Empty, Regex: true)]));

        var pipeline = FormattingPipeline.Create(profile);

        Assert.Equal([FormattingPhase.UserRulesBefore, FormattingPhase.UserRulesAfter], pipeline.Stages.Select(s => s.Phase));
        Assert.Equal("Vou a Tóquio.", pipeline.Format("Vou a Tokyo.  ").Text);
    }

    [Fact]
    public void Substituicao_que_cria_marcador_e_recusada_pelo_pipeline()
    {
        var pipeline = FormattingPipeline.Create(ProfileWith(new ReplacementRules([new Replacement("<br>", @"\n")], [])));

        Assert.Throws<FormattingException>(() => pipeline.Format("a<br>b"));
    }
}
