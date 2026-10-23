using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class EncodingReporterTests
{
    private static TextUnit Unit(string id, string translation, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, status);

    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts(params (string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units);

    [Fact]
    public void Shift_jis_recusa_os_tres_caracteres_sem_substituto()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit("a:000000", "«ªã")])), new ShiftJisTextEncoder());

        Assert.True(report.HasBlocking);
        Assert.Equal(["«", "ª", "ã"], report.Issues.Select(i => i.Character));
        Assert.Equal([0, 1, 2], report.Issues.Select(i => i.Position));
        Assert.Equal(["U+00AB", "U+00AA", "U+00E3"], report.Issues.Select(i => i.CodePoint));
        Assert.All(report.Issues, i => Assert.Null(i.Replacement));
        Assert.Empty(report.Replaced);
    }

    [Fact]
    public void Fold_troca_os_tres_e_informa_o_substituto_sem_bloquear()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit("a:000000", "«ªã")])), new FoldTextEncoder());

        Assert.False(report.HasBlocking);
        Assert.Equal(["\"", "a", "a"], report.Replaced.Select(i => i.Replacement));
        Assert.Equal([0, 1, 2], report.Replaced.Select(i => i.Position));
        Assert.Equal("fold", report.Strategy);
    }

    [Fact]
    public void Cp1252_representa_os_tres_como_estao_e_recusa_o_i_trema()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit("a:000000", "«ªãï")])), new Cp1252TextEncoder());

        var issue = Assert.Single(report.Issues);
        Assert.Equal("ï", issue.Character);
        Assert.Equal(3, issue.Position);
        Assert.Null(issue.Replacement);
    }

    [Fact]
    public void Cp1252_com_os_tres_deixa_o_relatorio_vazio()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit("a:000000", "«ªã")])), new Cp1252TextEncoder());

        Assert.Empty(report.Issues);
        Assert.False(report.HasBlocking);
        Assert.Equal(1, report.CheckedLines);
    }

    [Fact]
    public void Junta_todas_as_linhas_e_scripts_na_ordem_da_chave_e_da_linha()
    {
        var texts = Texts(
            ("b", [Unit("b:000000", "ã")]),
            ("a", [Unit("a:000000", "ok"), Unit("a:000001", "xã«")]));

        var report = EncodingReporter.Create(texts, new ShiftJisTextEncoder());

        Assert.Equal(["a:000001", "a:000001", "b:000000"], report.Issues.Select(i => i.UnitId));
        Assert.Equal([1, 2, 0], report.Issues.Select(i => i.Position));
        Assert.Equal(["a", "a", "b"], report.Issues.Select(i => i.ScriptKey));
        Assert.Equal(3, report.CheckedLines);
    }

    [Fact]
    public void Ignora_unidade_sem_traducao_a_gravar()
    {
        var texts = Texts(("a", [
            Unit("a:000000", "ã", TranslationStatus.Untranslated),
            Unit("a:000001", "原文"),
            Unit("a:000002", string.Empty)]));

        var report = EncodingReporter.Create(texts, new ShiftJisTextEncoder());

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.CheckedLines);
    }

    [Fact]
    public void Par_substituto_conta_uma_vez_com_o_ponto_de_codigo_inteiro()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit("a:000000", "x😀")])), new ShiftJisTextEncoder());

        var issue = Assert.Single(report.Issues);
        Assert.Equal("😀", issue.Character);
        Assert.Equal(1, issue.Position);
        Assert.Equal("U+1F600", issue.CodePoint);
    }

    [Fact]
    public void Argumentos_nulos_sao_recusados()
    {
        Assert.Throws<ArgumentNullException>(() => EncodingReporter.Create(null!, new ShiftJisTextEncoder()));
        Assert.Throws<ArgumentNullException>(() => EncodingReporter.Create(Texts(), null!));
    }

    [Fact]
    public void Fold_separa_bloqueantes_de_trocados_na_mesma_linha()
    {
        var report = EncodingReporter.Create(Texts(("a", [Unit("a:000000", "«Ola ª ã 😀")])), new FoldTextEncoder());

        Assert.Equal("😀", Assert.Single(report.Blocking).Character);
        Assert.Contains(report.Replaced, i => i.Character == "ã");
    }
}
