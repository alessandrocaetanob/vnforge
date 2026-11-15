using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public sealed class CompletenessRulesTests
{
    private static TextUnit Unit(string source, string? translation, TranslationStatus status, string id = "a:000001") =>
        new(id, TextUnitKind.Dialogue, null, source, translation, status);

    private static IReadOnlyList<QaIssue> Run(MarkerMismatchQaRule rule, params TextUnit[] units) =>
        [.. rule.Check(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units }))];

    [Fact]
    public void Marcador_nao_dispara_quando_o_original_tem_marcador_invalido()
    {
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"quebrado\x", "ok", TranslationStatus.Translated)));
    }
}
