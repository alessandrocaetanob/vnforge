using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

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

    [Fact]
    public void Falante_escolhido_vale_no_lugar_do_extraido()
    {
        var rule = new SpeakerEncodingRule();

        Assert.Empty(rule.Check(Context(Unit("犬井") with { ChosenSpeaker = "Inui" })));
        Assert.Empty(rule.Check(Context(Unit("犬井") with { ChosenSpeaker = string.Empty })));
        var issue = Assert.Single(rule.Check(Context(Unit("Inui") with { ChosenSpeaker = "犬井" })));
        Assert.Contains("犬井", issue.Message, StringComparison.Ordinal);
        Assert.Equal("a:1", issue.UnitId);
    }
}
