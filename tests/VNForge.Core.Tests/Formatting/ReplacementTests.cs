using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

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

    [Fact]
    public void Regex_invalida_em_programa_vira_FormattingException_com_o_caminho()
    {
        var profile = ProfileWith(new ReplacementRules([new Replacement("ok", "x"), new Replacement("(aberto", "y", Regex: true)], []));

        var ex = Assert.Throws<FormattingException>(() => FormattingPipeline.Create(profile));

        Assert.Contains("$.replacements.before[1].pattern", ex.Message);
    }

    [Fact]
    public void Regex_catastrofica_estoura_o_timeout_em_vez_de_travar()
    {
        var pipeline = FormattingPipeline.Create(ProfileWith(new ReplacementRules([], [new Replacement("(a+)+$", "x", Regex: true)])));

        var ex = Assert.Throws<FormattingException>(() => pipeline.Format(new string('a', 60) + "!"));

        Assert.Contains("$.replacements.after[0].pattern", ex.Message);
    }

    [Fact]
    public void Regex_invalida_no_json_aponta_a_regra_no_formatting_json()
    {
        const string json = """
            { "replacements": { "before": [
                { "pattern": "a", "with": "b" },
                { "pattern": "c", "with": "d" },
                { "pattern": "(aberto", "with": "x", "regex": true } ] } }
            """;

        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal("$.replacements.before[2].pattern", ex.Field);
        Assert.Contains("formatting.json inválido em $.replacements.before[2].pattern", ex.Message);
        Assert.Contains("expressão regular", ex.Message);
    }

    [Theory]
    [InlineData("""{ "replacements": { "after": [ { "pattern": "a" } ] } }""", "$.replacements.after[0].with")]
    [InlineData("""{ "replacements": { "after": [ { "pattern": "a", "with": 3 } ] } }""", "$.replacements.after[0].with")]
    [InlineData("""{ "replacements": { "after": "a" } }""", "$.replacements.after")]
    [InlineData("""{ "replacements": { "antes": [] } }""", "$.replacements.antes")]
    public void Substituicao_malformada_aponta_o_campo(string json, string field)
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal(field, ex.Field);
    }
}
