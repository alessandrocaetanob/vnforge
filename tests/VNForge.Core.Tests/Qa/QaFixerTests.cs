using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

[Trait("Risco", "R4")]
public sealed class QaFixerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public QaFixerTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Unit(string id, string? translation, TranslationStatus status = TranslationStatus.Translated, params string[] flags) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, status) { Flags = flags };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts(params TextUnit[] units) =>
        new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units };

    [Fact]
    public void Filtro_com_regra_sem_correcao_falha()
    {
        Assert.Throws<VNForgeException>(() => QaFixer.Plan(Texts(Unit("a:1", "x")), QaEngine.Default, ["marcador-diferente"]));
    }
    private static QaReport Run(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts) =>
        QaEngine.Default.Run(QaContext.ForTexts(texts));
}
