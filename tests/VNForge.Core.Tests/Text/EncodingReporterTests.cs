using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class EncodingReporterTests
{
    private const string TextId0 = "a:000000";
    private const string TextId1 = "a:000001";

    private static TextUnit Unit(string id, string translation, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts(params (string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units);

    [Fact]
    public void Fold_separa_bloqueantes_de_trocados_na_mesma_linha()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit(TextId0, "«Ola ª ã 😀")])), new FoldTextEncoder());

        Assert.Equal("😀", Assert.Single(report.Blocking).Character);
        Assert.Contains(report.Replaced, i => i.Character == "ã");
    }
}
