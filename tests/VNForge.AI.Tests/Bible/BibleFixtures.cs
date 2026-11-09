using VNForge.AI.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Bible;

internal static class BibleFixtures
{
    public static TextUnit Line(string key, int n, string source, string? speaker = null, string? translation = null) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, speaker, source, translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Machine);

    public static IReadOnlyList<TextUnit> Script(string key, params string[] sources) =>
        [.. sources.Select((s, i) => Line(key, i, s, i % 2 == 0 ? "美咲" : "健太"))];

    public static BibleSceneInput Scene(string key, string label, params string[] sources) => new(key, label, Script(key, sources));

    public static BibleChapterInput Chapter(string name, int? number, params BibleSceneInput[] scenes) =>
        new(name, number, scenes, [new BibleCharacterName("美咲", "Misaki")]);

    public static ProjectFolder NewProject(string root) => ProjectFolder.Create(
        root,
        new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
}
