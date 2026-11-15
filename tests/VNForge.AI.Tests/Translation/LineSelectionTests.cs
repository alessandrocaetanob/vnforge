using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Translation;

[Trait("Risco", "R2")]
public sealed class LineSelectionTests : IDisposable
{
    private const string Key = "yst00001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectFolder _project;

    public LineSelectionTests()
    {
        Directory.CreateDirectory(_root);
        _project = ProjectFolder.Create(_root, new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Key, [.. Enumerable.Range(0, 5).Select(i => new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", null, TranslationStatus.Untranslated))]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Id(int i) => $"{Key}:{i:D6}";

    private LineSelection Select(Func<TextUnit, bool>? wanted = null) =>
        LineSelection.Build(_project.LoadAllText(), null, null, wanted ?? (u => u.Status == TranslationStatus.Untranslated));

    private static TextUnit? Machine(TranslationResult result, TextUnit unit) =>
        unit with { Translation = result.Translation, Status = TranslationStatus.Machine };

    [Fact]
    public void Original_trocado_depois_da_selecao_pula_a_linha()
    {
        var selection = Select();
        var units = _project.LoadText(Key).ToList();
        units[2] = units[2] with { Source = "novo original" };
        _project.SaveText(Key, units);

        var saved = selection.ApplyAndSave(_project, [new TranslationResult(Id(2), "dois")], Machine);

        Assert.Equal(1, saved.Skipped);
        Assert.Null(_project.LoadText(Key)[2].Translation);
    }
}
