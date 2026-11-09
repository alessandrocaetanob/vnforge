using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Eval;

/// <summary>
/// Projeto de 65 linhas num script só: as 5 primeiras são traduções de máquina (contexto), depois vêm, de três em três, uma aprovada
/// (a original <c>原文i</c> com a tradução <c>Tradução i</c>), uma sem tradução e uma de máquina. São 20 aprovadas e 20 sem tradução.
/// </summary>
internal sealed class EvalProject : IDisposable
{
    public const string Key = "yst00001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public EvalProject(bool withGold = true)
    {
        Directory.CreateDirectory(_root);
        Folder = ProjectFolder.Create(_root, new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        Folder.SaveText(Key, [.. Enumerable.Range(0, 65).Select(i => Unit(i, withGold))]);
    }

    public ProjectFolder Folder { get; }

    public static string Id(int i) => $"{Key}:{i:D6}";

    public static IEnumerable<int> GoldIndexes => Enumerable.Range(0, 20).Select(k => 5 + 3 * k);

    public string TextJson => File.ReadAllText(Folder.TextPath(Key));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Um provedor que devolve a tradução aprovada da linha, o que dá chrF 100.</summary>
    public static FakeTranslationProvider Perfect() => new(unit => $"Tradução {unit.Source[2..]}");

    private static TextUnit Unit(int i, bool withGold)
    {
        var source = $"原文{i}";
        if (i < 5)
        {
            return new TextUnit(Id(i), TextUnitKind.Dialogue, "Misaki", source, $"Contexto {i}", TranslationStatus.Machine);
        }

        return ((i - 5) % 3) switch
        {
            0 when withGold => new TextUnit(Id(i), TextUnitKind.Dialogue, "Misaki", source, $"Tradução {i}", TranslationStatus.Approved),
            0 or 1 => new TextUnit(Id(i), TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated),
            _ => new TextUnit(Id(i), TextUnitKind.Dialogue, null, source, $"Máquina {i}", TranslationStatus.Machine),
        };
    }
}
