using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public sealed class QaEngineTests
{
    private static TextUnit Unit(string id, string? translation, params string[] flags) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated) { Flags = flags };

    private static QaContext Context(params TextUnit[] units) =>
        QaContext.ForTexts(units.GroupBy(u => u.Id.Split(':')[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)g.ToList()));

    [Fact]
    public void DescribeUntranslated_usa_singular_plural_e_fica_vazio_sem_linhas()
    {
        static QaReport Report(int n) => new QaEngine([new UntranslatedQaRule()]).Run(Context(
            [.. Enumerable.Range(1, n).Select(i => new TextUnit($"a:{i}", TextUnitKind.Dialogue, null, "こんにちは", null, TranslationStatus.Untranslated))]));

        Assert.Equal(string.Empty, QaBlockedException.DescribeUntranslated(Report(0)));
        Assert.Equal("QA: 1 linha sem tradução; o build copiou o original nela.", QaBlockedException.DescribeUntranslated(Report(1)));
        Assert.Equal("QA: 1.254 linhas sem tradução; o build copiou o original nelas.", QaBlockedException.DescribeUntranslated(Report(1254)));
    }
}
