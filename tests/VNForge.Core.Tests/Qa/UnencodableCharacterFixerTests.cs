using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public sealed class UnencodableCharacterFixerTests
{
    private static QaContext Context(string strategy = "cp1252", IReadOnlyList<TextUnit>? units = null) =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units ?? [] },
            [],
            [],
            FormattingProfile.Default,
            TextEncoderRegistry.Default.Create(strategy));

    private static TextUnit Unit(string translation) => new("a:1", TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated);

    private static ProjectFolder NovoProjeto(string raiz, string estrategia = "cp1252") => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig(estrategia),
        AiConfig.Default));

    [Fact]
    public void Sem_encoder_o_corretor_nao_muda_nada()
    {
        var rule = new UnencodableCharacterRule();
        var semEncoder = QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>>());

        Assert.Equal("Oi。", rule.Fix("Oi。", semEncoder));
        Assert.Equal("Oi。", ((IQaFixer)rule).Fix("Oi。"));
    }
}
