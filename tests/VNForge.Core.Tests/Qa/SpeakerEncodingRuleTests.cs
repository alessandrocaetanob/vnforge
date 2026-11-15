using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public sealed class SpeakerEncodingRuleTests
{
    private static TextUnit Unit(string? speaker, string? translation = "Bom dia!", TranslationStatus status = TranslationStatus.Translated) =>
        new("a:1", TextUnitKind.Dialogue, speaker, "おはよう", translation, status);

    private static QaContext Context(TextUnit unit, string? strategy = "cp1252", params CharacterEntry[] characters) =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] },
            [],
            characters,
            FormattingProfile.Default,
            strategy is null ? null : TextEncoderRegistry.Default.Create(strategy));

    [Fact]
    public void Sem_falante_ou_sem_encoder_ou_em_estrategia_que_grava_nao_dispara()
    {
        var rule = new SpeakerEncodingRule();

        Assert.Empty(rule.Check(Context(Unit(null))));
        Assert.Empty(rule.Check(Context(Unit("犬井"), strategy: null)));
        Assert.Empty(rule.Check(Context(Unit("犬井"), strategy: "sjis")));
    }
}
