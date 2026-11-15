using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public class GameTitleEncodingRuleTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> NoTexts = new Dictionary<string, IReadOnlyList<TextUnit>>();

    private static IReadOnlyList<QaIssue> Run(string strategy, GameTitleConfig? title, bool withEncoder = true)
    {
        var encoder = withEncoder ? TextEncoderRegistry.Default.Create(strategy) : null;
        var context = new QaContext(NoTexts, [], [], FormattingProfile.Default, encoder) { GameTitle = title };
        return [.. new GameTitleEncodingRule().Check(context)];
    }

    [Fact]
    public void Titulo_com_acento_em_sjis_e_erro_que_cita_o_caractere()
    {
        var issue = Assert.Single(Run("sjis", new GameTitleConfig("原題", "Título")));

        Assert.Equal(GameTitleEncodingRule.RuleId, issue.RuleId);
        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Equal(GameTitleText.UnitId, issue.UnitId);
        Assert.Contains("í", issue.Message, StringComparison.Ordinal);
        Assert.Contains("sjis", issue.Message, StringComparison.Ordinal);
    }
}

