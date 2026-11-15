using VNForge.Core.Formatting;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
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
}
