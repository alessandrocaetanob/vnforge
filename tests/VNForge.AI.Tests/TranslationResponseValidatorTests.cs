using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class TranslationResponseValidatorTests
{
    private static TextUnit Unit(string id, string source) =>
        new(id, TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    private static TranslationBatch Batch(params (string Id, string Source)[] lines) =>
        new([.. lines.Select(l => Unit(l.Id, l.Source))]);

    [Fact]
    public void Resposta_correta_nao_tem_problemas()
    {
        var batch = Batch(("a", @"Oi\n{VAR:x}\p"), ("b", "Tchau"));
        var report = TranslationResponseValidator.Validate(batch,
            [new("a", @"Hi\n{VAR:x}\p"), new("b", "Bye")]);

        Assert.Empty(report.Problems);
        Assert.Equal(["a", "b"], report.Valid.Select(r => r.Id));
    }

    [Fact]
    public void Id_ausente_e_problema()
    {
        var report = TranslationResponseValidator.Validate(Batch(("a", "x"), ("b", "y")), [new("a", "x2")]);

        var problem = Assert.Single(report.Problems);
        Assert.Equal("b", problem.Id);
        Assert.Equal(TranslationProblemKind.MissingId, problem.Kind);
    }

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

    [Theory]
    [InlineData(@"Hi\p {oops")]
    [InlineData(@"Hi\p\")]
    public void Chave_sem_fechar_ou_barra_final_sao_problemas(string translation)
    {
        var report = TranslationResponseValidator.Validate(Batch(("a", @"Oi\p")), [new("a", translation)]);

        Assert.Single(report.Problems);
    }

    [Fact]
    public void Id_duplicado_e_traducao_vazia_sao_problemas()
    {
        var batch = Batch(("a", "x"), ("b", "y"));
        var report = TranslationResponseValidator.Validate(batch, [new("a", "x"), new("a", "x"), new("b", "  ")]);

        Assert.Equal(
            [("a", TranslationProblemKind.DuplicateId), ("b", TranslationProblemKind.EmptyTranslation)],
            report.Problems.Select(p => (p.Id, p.Kind)));
    }

    [Fact]
    public void Id_desconhecido_e_ignorado_e_recusa_passa()
    {
        var batch = Batch(("a", @"x\p"));
        var report = TranslationResponseValidator.Validate(batch, [new("zzz", "?"), new("a", string.Empty, Refused: true)]);

        Assert.Empty(report.Problems);
        Assert.True(Assert.Single(report.Valid).Refused);
    }
}

public class ValidatingTranslationProviderTests
{
    private static TextUnit Unit(string id, string source) =>
        new(id, TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    private sealed class ScriptedProvider(Func<TranslationBatch, IReadOnlyList<TranslationResult>> respond) : ITranslationProvider
    {
        public List<TranslationBatch> Batches { get; } = [];

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            Batches.Add(batch);
            return Task.FromResult(respond(batch));
        }
    }
}
