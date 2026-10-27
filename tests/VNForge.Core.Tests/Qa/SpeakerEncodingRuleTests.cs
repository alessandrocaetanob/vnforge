using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class SpeakerEncodingRuleTests
{
    private static TextUnit Unit(string? speaker, string? translation = "Bom dia!", TranslationStatus status = TranslationStatus.Translated) =>
        new("a:1", TextUnitKind.Dialogue, speaker, "おはよう", translation, status);

    private static QaContext Context(TextUnit unit, string? strategy = "cp1252", params CharacterEntry[] characters) =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] },
            [],
            characters,
            FormattingProfile.Default,
            strategy is null ? null : TextEncoderRegistry.Default.Create(strategy));

    [Fact]
    public void Falante_japones_sem_cadastro_na_cp1252_e_erro_na_linha_com_o_que_fazer()
    {
        var issue = Assert.Single(new SpeakerEncodingRule().Check(Context(Unit("犬井"))));

        Assert.Equal(SpeakerEncodingRule.RuleId, issue.RuleId);
        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Equal("a:1", issue.UnitId);
        Assert.Equal(
            "O nome do personagem 犬井 não existe na codificação do jogo (cp1252). Cadastre a tradução do nome no Glossário > Personagens.",
            issue.Message);
    }

    [Fact]
    public void Falante_cadastrado_nao_dispara()
    {
        Assert.Empty(new SpeakerEncodingRule().Check(Context(Unit("犬井"), "cp1252", new CharacterEntry("犬井", "Inui"))));
    }

    [Fact]
    public void Falante_so_de_sinais_largos_nao_dispara_porque_a_largura_e_normalizada()
    {
        Assert.Empty(new SpeakerEncodingRule().Check(Context(Unit("？？？"))));
    }

    [Fact]
    public void Linha_que_o_build_nao_grava_nao_dispara()
    {
        var rule = new SpeakerEncodingRule();

        Assert.Empty(rule.Check(Context(Unit("犬井", translation: null, status: TranslationStatus.Untranslated))));
        Assert.Empty(rule.Check(Context(Unit("犬井", status: TranslationStatus.Keep))));
        Assert.Empty(rule.Check(Context(Unit("犬井", translation: "おはよう"))));
    }

    [Fact]
    public void Sem_falante_ou_sem_encoder_ou_em_estrategia_que_grava_nao_dispara()
    {
        var rule = new SpeakerEncodingRule();

        Assert.Empty(rule.Check(Context(Unit(null))));
        Assert.Empty(rule.Check(Context(Unit("犬井"), strategy: null)));
        Assert.Empty(rule.Check(Context(Unit("犬井"), strategy: "sjis")));
    }

    [Fact]
    public void Faz_parte_das_regras_do_vnf_na_categoria_de_acentos()
    {
        Assert.Contains(QaEngine.Default.Rules, r => r.Id == SpeakerEncodingRule.RuleId);
        Assert.Equal(QaCategories.Accents, QaCategories.Of(SpeakerEncodingRule.RuleId));
    }
}
