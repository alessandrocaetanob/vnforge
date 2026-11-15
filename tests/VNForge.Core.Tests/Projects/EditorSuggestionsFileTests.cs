using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public sealed class EditorSuggestionsFileTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public EditorSuggestionsFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static EditorSuggestion Suggest(string key, string id, string translation, string proposed = "Vocês dois") =>
        new(key, id, EditorSuggestionKind.Fluency, translation, proposed, "Soa mais natural.", Noon);

    private static TextUnit Unit(string id, string? translation) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Machine);

    [Theory]
    [InlineData("{\"version\":1}")]
    public void Arquivo_ilegivel_ou_de_outra_versao_conta_como_vazio_sem_derrubar_o_Editor(string content)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(EditorSuggestionsFile.PathFor(_project), content);

        Assert.Empty(EditorSuggestionsFile.Load(_project).Suggestions);
    }
}
