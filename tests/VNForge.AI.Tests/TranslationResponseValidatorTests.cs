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

    [Fact]
    public void Ordem_dos_marcadores_pode_mudar()
    {
        var report = TranslationResponseValidator.Validate(Batch(("a", @"{VAR:a} e {VAR:b}\p")), [new("a", @"{VAR:b} and {VAR:a}\p")]);

        Assert.Empty(report.Problems);
    }

    [Fact]
    public void Barra_e_chave_literais_contam_como_marcadores()
    {
        var report = TranslationResponseValidator.Validate(Batch(("a", @"a\b \{c")), [new("a", "a b c")]);

        Assert.Single(report.Problems);
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

    [Fact]
    public async Task Reenvia_so_a_linha_com_marcador_perdido()
    {
        var calls = 0;
        var inner = new FakeTranslationProvider(u =>
        {
            // a primeira vez que "b" é traduzida, perde o \p
            return u.Id == "b" && Interlocked.Increment(ref calls) == 1 ? "sem marcador" : $"[PT] {u.Source}";
        });
        var provider = new ValidatingTranslationProvider(inner);
        var batch = new TranslationBatch([Unit("a", @"um\p"), Unit("b", @"dois\p")]) { StyleGuide = "informal" };

        var results = await provider.TranslateAsync(batch, CancellationToken.None);

        Assert.Equal(["a", "b"], results.Select(r => r.Id));
        Assert.Equal(@"[PT] dois\p", results[1].Translation);
        Assert.Equal(2, inner.ReceivedBatches.Count);
        var retry = inner.ReceivedBatches.Last();
        Assert.Equal(["b"], retry.Lines.Select(l => l.Id));
        Assert.Equal("informal", retry.StyleGuide);
    }

    [Fact]
    public async Task Reenvia_so_o_id_ausente()
    {
        var first = true;
        var inner = new ScriptedProvider(batch =>
        {
            var skip = first;
            first = false;
            return [.. batch.Lines.Where(l => !skip || l.Id != "b").Select(l => new TranslationResult(l.Id, l.Source))];
        });
        var provider = new ValidatingTranslationProvider(inner);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "x"), Unit("b", "y")]), CancellationToken.None);

        Assert.Equal(["a", "b"], results.Select(r => r.Id));
        Assert.Equal(["a", "b"], inner.Batches[0].Lines.Select(l => l.Id));
        Assert.Equal(["b"], inner.Batches[1].Lines.Select(l => l.Id));
    }

    [Fact]
    public async Task Esgotadas_as_tentativas_a_linha_volta_marcada_para_revisao()
    {
        var inner = new ScriptedProvider(batch => [.. batch.Lines.Select(l => new TranslationResult(l.Id, "sem marcador"))]);
        var provider = new ValidatingTranslationProvider(inner, maxAttempts: 3);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", @"x\p")]), CancellationToken.None);

        Assert.Equal(3, inner.Batches.Count);
        var result = Assert.Single(results);
        Assert.True(result.Refused);
        Assert.Equal(string.Empty, result.Translation);
    }

    [Fact]
    public async Task Lote_valido_faz_uma_chamada_so()
    {
        var inner = new FakeTranslationProvider();
        var provider = new ValidatingTranslationProvider(inner);

        await provider.TranslateAsync(new TranslationBatch([Unit("a", @"x\p")]), CancellationToken.None);

        Assert.Single(inner.ReceivedBatches);
    }

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
