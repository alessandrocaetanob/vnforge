using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Spelling;

[Trait("Risco", "R4")]
public sealed class SpellCheckerTests
{
    private static SpellChecker Checker => SpellChecker.Portuguese;

    private static string[] Wrong(string text, ProjectDictionary? project = null) => [.. Checker.Find(text, project).Select(span => span.Text)];

    [Fact]
    public void Dicionario_ilegivel_desliga_a_regra_com_uma_mensagem_e_nao_derruba_a_conferencia()
    {
        var unit = new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "Oi.", TranslationStatus.Translated);
        var context = QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] });
        var rule = new SpellingRule(() => throw new VNForgeException("arquivo corrompido"));

        var issue = Assert.Single(rule.Check(context));

        Assert.Equal("a:1", issue.UnitId);
        Assert.Contains("desligada", issue.Message);
        Assert.Contains("arquivo corrompido", issue.Message);
        Assert.False(new QaReport([issue], [], []).Blocks(allowWarnings: false));
    }
}
