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
    public void Default_registra_as_tres_regras()
    {
        Assert.NotNull(QaEngine.Default.Find("nao-traduzida"));
        Assert.NotNull(QaEngine.Default.Find("traducao-vazia"));
        Assert.NotNull(QaEngine.Default.Find("marcador-diferente"));
    }

    [Fact]
    public void NaoTraduzida_dispara_em_linha_sem_traducao()
    {
        var issues = Run(new UntranslatedQaRule(), Unit("こんにちは", null, TranslationStatus.Untranslated));

        var issue = Assert.Single(issues);
        Assert.Equal("nao-traduzida", issue.RuleId);
        Assert.Equal(QaSeverity.Warning, issue.Severity);
        Assert.Equal("a:000001", issue.UnitId);
        Assert.Equal("a", issue.ScriptKey);
    }

    [Fact]
    public void NaoTraduzida_dispara_com_traducao_em_branco_e_status_untranslated()
    {
        Assert.Single(Run(new UntranslatedQaRule(), Unit("こんにちは", "  ", TranslationStatus.Untranslated)));
    }

    [Fact]
    public void NaoTraduzida_nao_dispara_em_linha_traduzida()
    {
        Assert.Empty(Run(new UntranslatedQaRule(), Unit("こんにちは", "Olá", TranslationStatus.Translated)));
    }

    [Fact]
    public void NaoTraduzida_nao_dispara_quando_o_original_e_vazio()
    {
        Assert.Empty(Run(new UntranslatedQaRule(), Unit("", null, TranslationStatus.Untranslated)));
    }

    [Fact]
    public void NaoTraduzida_nao_dispara_em_rascunho_ignorado()
    {
        // Rascunho em untranslated tem o aviso da regra de tradução ignorada, não esta.
        Assert.Empty(Run(new UntranslatedQaRule(), Unit("こんにちは", "Olá", TranslationStatus.Untranslated)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TraducaoVazia_dispara_com_status_de_traduzida_e_texto_vazio(string? translation)
    {
        var issues = Run(new EmptyTranslationQaRule(), Unit("こんにちは", translation, TranslationStatus.Machine));

        var issue = Assert.Single(issues);
        Assert.Equal("traducao-vazia", issue.RuleId);
        Assert.Equal(QaSeverity.Error, issue.Severity);
    }

    [Fact]
    public void TraducaoVazia_nao_dispara_com_texto()
    {
        Assert.Empty(Run(new EmptyTranslationQaRule(), Unit("こんにちは", "Olá", TranslationStatus.Reviewed)));
    }

    [Fact]
    public void TraducaoVazia_nao_dispara_em_linha_nao_traduzida()
    {
        Assert.Empty(Run(new EmptyTranslationQaRule(), Unit("こんにちは", "", TranslationStatus.Untranslated)));
    }

    [Fact]
    public void TraducaoVazia_nao_dispara_quando_o_original_tambem_e_vazio()
    {
        Assert.Empty(Run(new EmptyTranslationQaRule(), Unit("", "", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_dispara_quando_falta_um_marcador()
    {
        var issues = Run(new MarkerMismatchQaRule(), Unit(@"こんにちは\p次", @"Olá próxima", TranslationStatus.Translated));

        var issue = Assert.Single(issues);
        Assert.Equal("marcador-diferente", issue.RuleId);
        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Contains(@"\p", issue.Message);
        Assert.Contains("falta", issue.Message);
    }

    [Fact]
    public void Marcador_dispara_quando_sobra_um_marcador()
    {
        var issues = Run(new MarkerMismatchQaRule(), Unit("こんにちは", @"Olá\c", TranslationStatus.Translated));

        var issue = Assert.Single(issues);
        Assert.Contains(@"\c", issue.Message);
        Assert.Contains("sobra", issue.Message);
    }

    [Fact]
    public void Marcador_dispara_quando_a_variavel_muda()
    {
        Assert.Single(Run(new MarkerMismatchQaRule(), Unit("{VAR:name}さん", "{VAR:nome}", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_dispara_quando_a_contagem_muda()
    {
        Assert.Single(Run(new MarkerMismatchQaRule(), Unit(@"a\pb\pc", @"a\pb c", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_dispara_com_marcador_invalido_na_traducao()
    {
        var issues = Run(new MarkerMismatchQaRule(), Unit("こんにちは", @"Olá \x", TranslationStatus.Translated));

        Assert.Contains("inválido", Assert.Single(issues).Message);
    }

    [Fact]
    public void Marcador_nao_dispara_quando_os_marcadores_batem_em_outra_ordem()
    {
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit("{EF:01}と{RAW:ff}", "{RAW:ff} e {EF:01}", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_nao_dispara_quando_so_as_quebras_de_linha_mudam()
    {
        // As quebras são refeitas pela quebra de linha do build, então o tradutor não precisa copiá-las.
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"こんに\nちは\p次", @"Olá, tudo bem?\p próxima", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_nao_dispara_em_linha_sem_traducao()
    {
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"こんにちは\p", null, TranslationStatus.Untranslated)));
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"こんにちは\p", "", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_nao_dispara_quando_o_original_tem_marcador_invalido()
    {
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"quebrado\x", "ok", TranslationStatus.Translated)));
    }

    [Fact]
    public void Marcador_trata_barra_escapada_como_marcador()
    {
        Assert.Empty(Run(new MarkerMismatchQaRule(), Unit(@"a\\b", @"c\\d", TranslationStatus.Translated)));
        Assert.Single(Run(new MarkerMismatchQaRule(), Unit(@"a\\b", "c d", TranslationStatus.Translated)));
    }

    [Fact]
    public void Aviso_marcado_como_ok_some_do_relatorio_mas_erro_continua()
    {
        var warn = Unit("こんにちは", null, TranslationStatus.Untranslated, "a:000001") with { Flags = ["nao-traduzida"] };
        var error = Unit("こんにちは", "", TranslationStatus.Machine, "a:000002") with { Flags = ["traducao-vazia"] };

        var report = QaEngine.Default.Run(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [warn, error] }));

        Assert.Single(report.Silenced);
        Assert.Single(report.Issues);
        Assert.Equal("traducao-vazia", report.Issues[0].RuleId);
    }
}
