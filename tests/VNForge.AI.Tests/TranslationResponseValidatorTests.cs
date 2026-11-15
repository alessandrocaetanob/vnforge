using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

[Trait("Risco", "R4")]
public class TranslationResponseValidatorTests
{
    private static TextUnit Unit(string id, string source) =>
        new(id, TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    private static TranslationBatch Batch(params (string Id, string Source)[] lines) =>
        new([.. lines.Select(l => Unit(l.Id, l.Source))]);

    [Fact]
    public void Marcador_perdido_e_problema()
    {
        var report = TranslationResponseValidator.Validate(Batch(("a", @"Oi\p")), [new("a", "Hi")]);

        var problem = Assert.Single(report.Problems);
        Assert.Equal(TranslationProblemKind.MarkersChanged, problem.Kind);
        Assert.Empty(report.Valid);
    }

    [Fact]
    public void Marcador_acrescentado_ou_trocado_e_problema()
    {
        var batch = Batch(("a", @"Oi\p"), ("b", @"Oi\n"));
        var report = TranslationResponseValidator.Validate(batch, [new("a", @"Hi\p\p"), new("b", @"Hi\p")]);

        Assert.Equal(["a", "b"], report.Problems.Select(p => p.Id));
    }
}

