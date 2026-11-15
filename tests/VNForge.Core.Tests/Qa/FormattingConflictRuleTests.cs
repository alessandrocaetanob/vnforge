using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public sealed class FormattingConflictRuleTests
{
    private static readonly FormattingProfile EllipsisAsCharacter =
        FormattingProfile.Default with { Typography = new TypographyRule(Ellipsis: EllipsisStyle.Character) };

    private static TextUnit Unit(string translation, string id = "a:1", TextUnitKind kind = TextUnitKind.Dialogue) =>
        new(id, kind, null, "原文", translation, TranslationStatus.Translated);

    private static QaContext Context(FormattingProfile profile, string strategy = "cp1252", params TextUnit[] units) =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units },
            [],
            [],
            profile,
            TextEncoderRegistry.Default.Create(strategy));

    private static ProjectFolder NewProject(string root, string strategy = "cp1252") => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig(strategy),
        AiConfig.Default));

    [Fact]
    public void Sem_conflito_nem_encoder_a_regra_cala()
    {
        Assert.Empty(new FormattingConflictRule().Check(Context(EllipsisAsCharacter, "sjis", Unit("Ah..."))));
        Assert.Empty(new FormattingConflictRule().Check(Context(FormattingProfile.Default, units: [Unit("Ah...")])));
        Assert.Empty(new FormattingConflictRule().Check(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [Unit("Ah...")] })));
    }
}
