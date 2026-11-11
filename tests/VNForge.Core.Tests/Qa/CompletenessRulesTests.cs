using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class CompletenessRulesTests
{
    private static TextUnit Unit(string source, string? translation, TranslationStatus status, string id = "a:000001") =>
        new(id, TextUnitKind.Dialogue, null, source, translation, status);

    private static IReadOnlyList<QaIssue> Run(IQaRule rule, params TextUnit[] units) =>
        [.. rule.Check(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units }))];

    [Fact]
    public void Marcador_dispara_quando_sobra_um_marcador()
    {
        var issues = Run(new MarkerMismatchQaRule(), Unit("こんにちは", @"Olá\c", TranslationStatus.Translated));

        var issue = Assert.Single(issues);
        Assert.Contains(@"\c", issue.Message);
        Assert.Contains("sobra", issue.Message);
    }

    [Fact]
    public void Marcador_dispara_com_marcador_invalido_na_traducao()
    {
        var issues = Run(new MarkerMismatchQaRule(), Unit("こんにちは", @"Olá \x", TranslationStatus.Translated));

        Assert.Contains("inválido", Assert.Single(issues).Message);
    }

    [Fact]
    public void PaginaExtra_avisa_quando_a_traducao_tem_um_p_a_mais()
    {
        var issue = Assert.Single(Run(new ExtraPageQaRule(), Unit(@"こんにちは\p", @"Olá\p tudo bem?\p", TranslationStatus.Translated)));

        Assert.Equal("pagina-extra", issue.RuleId);
        Assert.Equal(QaSeverity.Warning, issue.Severity);
        Assert.Equal("a:000001", issue.UnitId);
        Assert.Contains("1 virada de página a mais", issue.Message);
    }

    [Fact]
    public void PaginaExtra_conta_todas_as_viradas_que_sobram()
    {
        var issue = Assert.Single(Run(new ExtraPageQaRule(), Unit("こんにちは", @"a\pb\pc", TranslationStatus.Translated)));

        Assert.Contains("2 viradas de página a mais", issue.Message);
    }

    [Fact]
    public void PaginaExtra_nao_dispara_sem_traducao_nem_com_marcador_invalido()
    {
        Assert.Empty(Run(new ExtraPageQaRule(), Unit(@"こんにちは\p", null, TranslationStatus.Untranslated)));
        Assert.Empty(Run(new ExtraPageQaRule(), Unit(@"こんにちは\p", @"Olá\p\x\p", TranslationStatus.Translated)));
        Assert.Empty(Run(new ExtraPageQaRule(), Unit(@"quebrado\x", @"ok\p", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_nao_dispara_quando_o_original_tem_marcador_invalido()
    {
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"quebrado\x", "ok", TranslationStatus.Translated)));
    }
}
