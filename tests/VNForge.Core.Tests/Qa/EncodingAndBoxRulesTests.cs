using VNForge.Core.Formatting;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class EncodingAndBoxRulesTests
{
    private static TextUnit Unit(string translation, TextUnitKind kind = TextUnitKind.Dialogue) =>
        new("a:1", kind, null, "原文", translation, TranslationStatus.Translated);

    private static QaContext Context(TextUnit unit, FormattingProfile? profile = null, string? strategy = "sjis") =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] },
            [],
            [],
            profile ?? FormattingProfile.Default,
            strategy is null ? null : TextEncoderRegistry.Default.Create(strategy));

    [Fact]
    public void Substituto_solto_e_emoji_sao_apontados_sem_lancar()
    {
        var issues = new UnencodableCharacterRule().Check(Context(Unit("a\uD83D b 😀"))).ToList();

        Assert.Equal(2, issues.Count);
        Assert.Contains("U+D83D", issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("U+1F600", issues[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aspa_curva_e_reticencias_na_cp1252_disparam_com_o_motivo_e_a_troca()
    {
        var issues = new UnencodableCharacterRule().Check(Context(Unit("“Falando” disso…"), strategy: "cp1252")).ToList();

        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal(QaSeverity.Error, i.Severity));
        Assert.Contains("U+201C", issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("0x93", issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("aspas retas", issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("U+2026", issues[2].Message, StringComparison.Ordinal);
        Assert.Contains("...", issues[2].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("build", string.Concat(issues.Select(i => i.Message)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aspas_retas_e_reticencias_em_tres_pontos_da_formatacao_evitam_o_erro()
    {
        var profile = FormattingProfile.Default with { Typography = new TypographyRule(QuoteStyle.Ascii, EllipsisStyle.Dots) };

        Assert.Empty(new UnencodableCharacterRule().Check(Context(Unit("“Falando” ‘isso’…"), profile, "cp1252")));
    }

    // --- tamanho-em-bytes ---

    private static FormattingProfile Limit(TextUnitKind kind, int bytes) =>
        FormattingProfile.Default with { ByteLimits = new Dictionary<TextUnitKind, int> { [kind] = bytes } };

    [Fact]
    public void Texto_acima_do_limite_de_bytes_dispara_como_erro()
    {
        // Três caracteres japoneses ocupam 6 bytes em Shift-JIS.
        var issue = Assert.Single(new FieldByteLimitRule().Check(Context(Unit("あいう", TextUnitKind.Name), Limit(TextUnitKind.Name, 5))));

        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Contains("6 bytes", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Texto_no_limite_de_bytes_papel_sem_limite_ou_marcador_nao_dispara()
    {
        var profile = Limit(TextUnitKind.Name, 6);

        Assert.Empty(new FieldByteLimitRule().Check(Context(Unit("あいう", TextUnitKind.Name), profile)));
        Assert.Empty(new FieldByteLimitRule().Check(Context(Unit("あいうえお", TextUnitKind.Choice), profile)));
        Assert.Empty(new FieldByteLimitRule().Check(Context(Unit("あいう{VAR:nome}\\n", TextUnitKind.Name), profile)));
    }

    [Fact]
    public void Limite_de_bytes_deixa_o_caractere_nao_codificavel_para_a_outra_regra()
    {
        Assert.Empty(new FieldByteLimitRule().Check(Context(Unit("Ação longa", TextUnitKind.Name), Limit(TextUnitKind.Name, 3))));
    }

    [Fact]
    public void O_limite_de_bytes_vai_e_volta_no_formatting_json()
    {
        var profile = FormattingProfileSerializer.Parse("""{ "byteLimits": { "name": 16, "choice": 32 } }""");

        Assert.Equal(16, profile.ByteLimits[TextUnitKind.Name]);
        Assert.Equal(32, profile.ByteLimits[TextUnitKind.Choice]);
        Assert.Equal(profile.ByteLimits, FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile)).ByteLimits);
        Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse("""{ "byteLimits": { "name": 0 } }"""));
        Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse("""{ "byteLimits": { "nome": 8 } }"""));
    }
}
