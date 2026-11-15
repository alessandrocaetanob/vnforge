using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Formatting;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>#536: o jogo do VNDB confirmado vira <c>vndb.json</c> e os personagens que casam com o <c>ES.CHAR.NAME</c> chegam ao <c>characters.json</c> com o gênero.</summary>
[Trait("Risco", "R7")]
public sealed class VndbProjectLinkerTests
{
    private static FakeVndbCatalog Catalog(params VndbVisualNovel[] games) =>
        new(games.Length == 0 ? [VndbSamples.Game(description: "Numa cidade onde chove, [spoiler]ele morre[/spoiler]Kenta [b]volta[/b].")] : games, VndbSamples.Cast);

    private static ProjectFolder NewProject(string root) => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Name(int n, string source) =>
        new($"yst00001:{n:D6}", TextUnitKind.Name, null, source, null, TranslationStatus.Untranslated);

    private static TextUnit Line(int n, string speaker) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, "こんにちは", "Olá", TranslationStatus.Translated);

    private static ProjectFolder ProjectWithGame(string root, string package)
    {
        var game = Path.Combine(root, "jogo");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "pacote.fake"), package);
        return ProjectFolder.Create(
            Path.Combine(root, "projeto"),
            new ProjectConfig("Teste", new GameConfig(game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("cp1252"), AiConfig.Default));
    }

    [Fact]
    public async Task ApplyCharacters_com_characters_json_ilegivel_recusa_e_deixa_o_arquivo_intacto()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太")]);
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));
        File.WriteAllText(project.CharactersPath, "{ nao e json");

        Assert.Throws<GlossaryFileException>(() => VndbProjectLinker.ApplyCharacters(project));

        Assert.Equal("{ nao e json", File.ReadAllText(project.CharactersPath));
    }

    private static QaContext Qa(ProjectFolder project, IReadOnlyList<CharacterEntry> characters) =>
        new(project.LoadAllText(), [], characters, FormattingProfile.Default);
}
