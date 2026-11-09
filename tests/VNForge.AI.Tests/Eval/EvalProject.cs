using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Eval;

/// <summary>
/// Projeto de um script só: as 5 primeiras linhas são traduções de máquina (contexto), depois vêm as aprovadas (a original <c>原文i</c> com a tradução <c>Tradução i</c>),
/// seguidas, e depois 20 sem tradução e 20 de máquina. Com <c>spaced</c>, as aprovadas ficam de três em três, com uma sem tradução e uma de máquina no meio.
/// </summary>
internal sealed class EvalProject : IDisposable
{
    public const string Key = "yst00001";

    private const int ContextLines = 5;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));
    private readonly int _goldCount;
    private readonly bool _spaced;

    public EvalProject(bool withGold = true, int goldCount = 20, bool spaced = false)
    {
        _goldCount = goldCount;
        _spaced = spaced;
        Directory.CreateDirectory(_root);
        Folder = ProjectFolder.Create(_root, new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        Folder.SaveText(Key, [.. Enumerable.Range(0, Total).Select(i => Unit(i, withGold))]);
    }

    public ProjectFolder Folder { get; }

    public static string Id(int i) => $"{Key}:{i:D6}";

    public IEnumerable<int> GoldIndexes => Enumerable.Range(0, _goldCount).Select(k => ContextLines + (_spaced ? 3 * k : k));

    private int Total => ContextLines + (_spaced ? 3 * _goldCount : _goldCount + 40);

    public string TextJson => File.ReadAllText(Folder.TextPath(Key));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Um provedor que devolve a tradução aprovada da linha, o que dá chrF 100.</summary>
    public static FakeTranslationProvider Perfect() => new(unit => $"Tradução {unit.Source[2..]}");

    private TextUnit Unit(int i, bool withGold)
    {
        var source = $"原文{i}";
        if (i < ContextLines)
        {
            return new TextUnit(Id(i), TextUnitKind.Dialogue, "Misaki", source, $"Contexto {i}", TranslationStatus.Machine);
        }

        var k = i - ContextLines;
        if (_spaced)
        {
            return (k % 3) switch
            {
                0 when withGold => Approved(i, source),
                0 or 1 => Untranslated(i, source),
                _ => Machine(i, source),
            };
        }

        if (k < _goldCount)
        {
            return withGold ? Approved(i, source) : Untranslated(i, source);
        }

        return k - _goldCount < 20 ? Untranslated(i, source) : Machine(i, source);
    }

    private static TextUnit Approved(int i, string source) => new(Id(i), TextUnitKind.Dialogue, "Misaki", source, $"Tradução {i}", TranslationStatus.Approved);

    private static TextUnit Untranslated(int i, string source) => new(Id(i), TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    private static TextUnit Machine(int i, string source) => new(Id(i), TextUnitKind.Dialogue, null, source, $"Máquina {i}", TranslationStatus.Machine);
}
