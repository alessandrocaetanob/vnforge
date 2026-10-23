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

    private static FormattingProfile WithWrap(int maxWidth, int? maxLines, bool enabled) =>
        FormattingProfile.Default with { Wrap = new WrapRule(enabled, maxWidth, maxLines, CharWidthMode.Uniform) };

    // --- caractere-nao-codificavel ---

    [Fact]
    public void Caractere_fora_da_estrategia_dispara_como_erro()
    {
        var issue = Assert.Single(new UnencodableCharacterRule().Check(Context(Unit("Ola ç\\p{VAR:x}"))));

        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Equal("a:1", issue.UnitId);
        Assert.Contains("U+00E7", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Caractere_codificavel_nao_dispara()
    {
        Assert.Empty(new UnencodableCharacterRule().Check(Context(Unit("Ola\\n日本"))));
    }

    [Fact]
    public void Estrategia_que_substitui_nao_dispara_e_sem_encoder_nao_confere()
    {
        Assert.Empty(new UnencodableCharacterRule().Check(Context(Unit("Ação"), strategy: "fold")));
        Assert.Empty(new UnencodableCharacterRule().Check(Context(Unit("Ação"), strategy: null)));
    }

    [Fact]
    public void Substituto_solto_e_emoji_sao_apontados_sem_lancar()
    {
        var issues = new UnencodableCharacterRule().Check(Context(Unit("a\uD83D b 😀"))).ToList();

        Assert.Equal(2, issues.Count);
        Assert.Contains("U+D83D", issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("U+1F600", issues[1].Message, StringComparison.Ordinal);
    }

    // --- caixa-acima-do-limite ---

    [Fact]
    public void Caixa_com_mais_linhas_que_o_maximo_dispara_como_aviso()
    {
        var context = Context(Unit("um\\ndois\\ntres\\nquatro\\p1"), WithWrap(40, 3, enabled: false));

        var issue = Assert.Single(new BoxLineLimitRule().Check(context));

        Assert.Equal(QaSeverity.Warning, issue.Severity);
        Assert.Contains("caixa 1 com 4 linhas (máximo 3)", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Caixa_dentro_do_limite_ou_sem_limite_nao_dispara()
    {
        Assert.Empty(new BoxLineLimitRule().Check(Context(Unit("um\\ndois\\ntres\\p um\\ndois"), WithWrap(40, 3, enabled: false))));
        Assert.Empty(new BoxLineLimitRule().Check(Context(Unit("a\\nb\\nc\\nd\\ne"), WithWrap(40, null, enabled: false))));
    }

    [Fact]
    public void A_quebra_automatica_conta_nas_linhas_da_caixa()
    {
        var profile = WithWrap(10, 2, enabled: true);

        Assert.Single(new BoxLineLimitRule().Check(Context(Unit("aaaa bbbb cccc dddd eeee"), profile)));
        Assert.Empty(new BoxLineLimitRule().Check(Context(Unit("aaaa bbbb cccc"), profile)));
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

    [Fact]
    public void O_motor_padrao_registra_as_tres_regras()
    {
        Assert.Contains(QaEngine.Default.Rules, r => r.Id == UnencodableCharacterRule.RuleId);
        Assert.Contains(QaEngine.Default.Rules, r => r.Id == BoxLineLimitRule.RuleId);
        Assert.Contains(QaEngine.Default.Rules, r => r.Id == FieldByteLimitRule.RuleId);
    }
}
