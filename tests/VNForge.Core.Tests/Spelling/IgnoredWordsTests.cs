using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Spelling;

[Trait("Risco", "R3")]
public sealed class IgnoredWordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ignored-" + Guid.NewGuid().ToString("N"));

    public IgnoredWordsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ProjectFolder NovoProjeto() => ProjectFolder.Create(_root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static string[] Wrong(string text, ProjectDictionary project) => [.. SpellChecker.Portuguese.Find(text, project).Select(span => span.Text)];

    [Fact]
    public void Lista_ida_e_volta_byte_a_byte_com_uma_palavra_por_linha()
    {
        var json = IgnoredWordsSerializer.Serialize([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")]);

        Assert.Equal("{\n  \"version\": 1,\n  \"entries\": [\n    {\"word\":\"Zorgblat\"},\n    {\"word\":\"kyuun\"}\n  ]\n}\n", json);
        var lido = IgnoredWordsSerializer.Parse(json, "ignored-words.json");
        Assert.Equal([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")], lido);
        Assert.Equal(json, IgnoredWordsSerializer.Serialize(lido));
    }
}
