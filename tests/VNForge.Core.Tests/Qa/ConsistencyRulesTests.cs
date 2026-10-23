using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class ConsistencyRulesTests
{
    private static TextUnit Unit(string id, string source, string? translation, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, null, source, translation, status);

    private static QaContext Context(IReadOnlyList<GlossaryTerm> glossary, IReadOnlyList<CharacterEntry> characters, params TextUnit[] units) =>
        new(units.GroupBy(u => u.Id.Split(':')[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)g.ToList()),
            glossary, characters, FormattingProfile.Default);

    private static readonly GlossaryTerm[] Terms = [new("魔法", "magia")];
    private static readonly CharacterEntry[] Cast = [new("春花", "Haruka")];

    [Fact]
    public void Glossario_dispara_quando_o_termo_nao_aparece_traduzido()
    {
        var issues = new GlossaryTermRule().Check(Context(Terms, [], Unit("a:1", "魔法を使う", "Usa feitiço"))).ToList();

        var issue = Assert.Single(issues);
        Assert.Equal(("glossario-diferente", QaSeverity.Warning, "a:1"), (issue.RuleId, issue.Severity, issue.UnitId));
        Assert.Contains("magia", issue.Message);
    }

    [Fact]
    public void Glossario_nao_dispara_com_o_alvo_presente_sem_termo_ou_sem_traducao()
    {
        var rule = new GlossaryTermRule();

        Assert.Empty(rule.Check(Context(Terms, [], Unit("a:1", "魔法を使う", "Usa Magia"))));
        Assert.Empty(rule.Check(Context(Terms, [], Unit("a:2", "こんにちは", "Olá"))));
        Assert.Empty(rule.Check(Context(Terms, [], Unit("a:3", "魔法を使う", null, TranslationStatus.Untranslated))));
    }

    [Fact]
    public void Personagem_dispara_quando_o_nome_sai_diferente()
    {
        var issues = new CharacterNameRule().Check(Context([], Cast, Unit("a:1", "春花が来た", "Haru chegou"))).ToList();

        var issue = Assert.Single(issues);
        Assert.Equal("personagem-diferente", issue.RuleId);
        Assert.Contains("Haruka", issue.Message);
    }

    [Fact]
    public void Personagem_nao_dispara_com_o_nome_certo_ou_sem_o_nome_no_original()
    {
        var rule = new CharacterNameRule();

        Assert.Empty(rule.Check(Context([], Cast, Unit("a:1", "春花が来た", "Haruka chegou"))));
        Assert.Empty(rule.Check(Context([], Cast, Unit("a:2", "誰かが来た", "Alguém chegou"))));
    }

    [Fact]
    public void Inconsistencia_dispara_na_linha_que_foge_da_traducao_mais_usada_entre_scripts()
    {
        var context = Context([], [],
            Unit("a:1", "はい", "Sim"), Unit("b:1", "はい", "Sim"), Unit("b:2", "はい", "Isso"));

        var issue = Assert.Single(new InconsistentTranslationRule().Check(context));

        Assert.Equal(("traducao-inconsistente", QaSeverity.Warning, "b:2", "b"), (issue.RuleId, issue.Severity, issue.UnitId, issue.ScriptKey));
        Assert.Contains("Sim", issue.Message);
    }

    [Fact]
    public void Inconsistencia_nao_dispara_com_traducoes_iguais_originais_distintos_ou_linhas_sem_traducao()
    {
        var context = Context([], [],
            Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", " Sim "), Unit("a:3", "いいえ", "Não"),
            Unit("a:4", "はい", null, TranslationStatus.Untranslated));

        Assert.Empty(new InconsistentTranslationRule().Check(context));
    }

    [Fact]
    public void Personagem_com_espaco_ideografico_no_source_ainda_casa()
    {
        CharacterEntry[] cast = [new("春花　", "Haruka")];

        Assert.Single(new CharacterNameRule().Check(Context([], cast, Unit("a:1", "春花が来た", "Haru chegou"))));
    }

    [Fact]
    public void Inconsistencia_no_empate_toma_a_primeira_como_referencia()
    {
        var issue = Assert.Single(new InconsistentTranslationRule().Check(Context([], [], Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"))));

        Assert.Equal("a:2", issue.UnitId);
    }

    [Fact]
    public void Aviso_de_consistencia_some_quando_a_linha_esta_marcada_ok()
    {
        var engine = new QaEngine([new GlossaryTermRule()]);
        var unit = Unit("a:1", "魔法を使う", "Usa feitiço") with { Flags = ["glossario-diferente"] };

        var report = engine.Run(Context(Terms, [], unit));

        Assert.Empty(report.Issues);
        Assert.Single(report.Silenced);
    }

    [Fact]
    public void Default_registra_as_tres_regras()
    {
        Assert.NotNull(QaEngine.Default.Find("glossario-diferente"));
        Assert.NotNull(QaEngine.Default.Find("personagem-diferente"));
        Assert.NotNull(QaEngine.Default.Find("traducao-inconsistente"));
    }
}
