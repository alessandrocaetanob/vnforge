using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

public class ReplacementTests
{
    private static FormattingProfile ProfileWith(ReplacementRules rules) => FormattingProfile.Default with { Replacements = rules };

    [Fact]
    public void Substituicao_antes_roda_antes_das_regras_e_a_depois_roda_depois()
    {
        // "antes" troca A por B; a regra do perfil (espia) vê B e acrescenta "!"; "depois" troca B! por C.
        var before = new ReplacementStage("replacements-before", FormattingPhase.UserRulesBefore, [new Replacement("A", "B")]);
        var after = new ReplacementStage("replacements-after", FormattingPhase.UserRulesAfter, [new Replacement("B!", "C")]);
        var profileRule = new AppendStage("!", FormattingPhase.Typography);

        var result = new FormattingPipeline([after, profileRule, before]).Format("A");

        Assert.Equal("C", result.Text);
    }

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
    public void Perfil_sem_substituicoes_nao_registra_etapa()
    {
        Assert.Empty(FormattingPipeline.Create(FormattingProfile.Default).Stages);
    }

    [Fact]
    public void Literal_nao_interpreta_caracteres_de_regex_e_troca_todas_as_ocorrencias()
    {
        var pipeline = FormattingPipeline.Create(ProfileWith(new ReplacementRules([new Replacement("a.b", "X")], [])));

        Assert.Equal("X aXb a-b X", pipeline.Format("a.b aXb a-b a.b").Text);
    }

    [Fact]
    public void Regex_usa_grupos_na_substituicao()
    {
        var pipeline = FormattingPipeline.Create(ProfileWith(new ReplacementRules([new Replacement(@"(\w+)-san", "Sr. $1", Regex: true)], [])));

        Assert.Equal("Olá, Sr. Tanaka!", pipeline.Format("Olá, Tanaka-san!").Text);
    }

    [Fact]
    public void Substituicao_nunca_alcanca_marcadores_de_controle()
    {
        var pipeline = FormattingPipeline.Create(ProfileWith(new ReplacementRules([new Replacement("n", "N"), new Replacement(@"\w", "_", Regex: true)], [])));

        // \n, \c, {VAR:nome} e \p não podem mudar; só o texto comum muda.
        Assert.Equal(@"\c__\n{VAR:nome}\p_", pipeline.Format(@"\cno\n{VAR:nome}\pa").Text);
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
    public void Json_le_as_listas_e_serializa_de_volta()
    {
        const string json = """
            { "replacements": {
                "before": [ { "pattern": "Tokyo", "with": "Tóquio" }, { "pattern": "(\\w+)-san", "with": "Sr. $1", "regex": true } ],
                "after":  [ { "pattern": "  ", "with": " " } ] } }
            """;

        var profile = FormattingProfileSerializer.Parse(json);

        Assert.Equal([new Replacement("Tokyo", "Tóquio"), new Replacement(@"(\w+)-san", "Sr. $1", true)], profile.Replacements.Before);
        Assert.Equal([new Replacement("  ", " ")], profile.Replacements.After);
        var back = FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile));
        Assert.Equal(profile.Replacements.Before, back.Replacements.Before);
        Assert.Equal(profile.Replacements.After, back.Replacements.After);
    }

    [Fact]
    public void Perfil_sem_replacements_usa_listas_vazias()
    {
        var profile = FormattingProfileSerializer.Parse("{}");

        Assert.Empty(profile.Replacements.Before);
        Assert.Empty(profile.Replacements.After);
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
    [InlineData("""{ "replacements": { "before": [ { "pattern": "", "with": "x" } ] } }""", "$.replacements.before[0].pattern")]
    [InlineData("""{ "replacements": { "before": [ { "with": "x" } ] } }""", "$.replacements.before[0].pattern")]
    [InlineData("""{ "replacements": { "after": [ { "pattern": "a" } ] } }""", "$.replacements.after[0].with")]
    [InlineData("""{ "replacements": { "after": [ { "pattern": "a", "with": 3 } ] } }""", "$.replacements.after[0].with")]
    [InlineData("""{ "replacements": { "after": [ { "pattern": "a", "with": "b", "regex": "sim" } ] } }""", "$.replacements.after[0].regex")]
    [InlineData("""{ "replacements": { "after": [ { "pattern": "a", "with": "b", "extra": 1 } ] } }""", "$.replacements.after[0].extra")]
    [InlineData("""{ "replacements": { "after": "a" } }""", "$.replacements.after")]
    [InlineData("""{ "replacements": { "after": [ 1 ] } }""", "$.replacements.after[0]")]
    [InlineData("""{ "replacements": { "antes": [] } }""", "$.replacements.antes")]
    public void Substituicao_malformada_aponta_o_campo(string json, string field)
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal(field, ex.Field);
    }

    private sealed class AppendStage(string suffix, FormattingPhase phase) : TextRunStage
    {
        public override string Id => "append";

        public override FormattingPhase Phase => phase;

        protected override string ApplyToRun(string run, FormattingContext context) => run + suffix;
    }
}
