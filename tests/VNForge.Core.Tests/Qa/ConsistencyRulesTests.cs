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

    private const string MagicLine = "魔法を使う";

    private static readonly GlossaryTerm[] Terms = [new("魔法", "magia")];
    private static readonly CharacterEntry[] Cast = [new("春花", "Haruka")];

    [Fact]
    public void Glossario_dispara_quando_o_termo_nao_aparece_traduzido()
    {
        var issues = new GlossaryTermRule().Check(Context(Terms, [], Unit("a:1", MagicLine, "Usa feitiço"))).ToList();

        var issue = Assert.Single(issues);
        Assert.Equal(("glossario-diferente", QaSeverity.Warning, "a:1"), (issue.RuleId, issue.Severity, issue.UnitId));
        Assert.Contains("magia", issue.Message);
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
    public void Inconsistencia_dispara_na_linha_que_foge_da_traducao_mais_usada_entre_scripts()
    {
        var context = Context([], [],
            Unit("a:1", "はい", "Sim"), Unit("b:1", "はい", "Sim"), Unit("b:2", "はい", "Isso"));

        var issue = Assert.Single(new InconsistentTranslationRule().Check(context));

        Assert.Equal(("traducao-inconsistente", QaSeverity.Warning, "b:2", "b"), (issue.RuleId, issue.Severity, issue.UnitId, issue.ScriptKey));
        Assert.Contains("Sim", issue.Message);
    }
}
