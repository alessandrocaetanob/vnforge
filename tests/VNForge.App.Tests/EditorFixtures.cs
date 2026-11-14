using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Linhas sintéticas do Editor, geradas em código: nenhum arquivo de jogo entra nos testes.</summary>
internal static class EditorFixtures
{
    private static readonly TranslationStatus[] Statuses = [.. Enum.GetValues<TranslationStatus>().Where(s => s != TranslationStatus.Keep)];

    public static ProjectSummary Summary(string name = "Minha VN", string root = "C:\\Projetos\\MinhaVN") =>
        new(name, root, "yuris", 500, "fold", "claude-opus-5-5", 1, new StatusCounts(0, 0, 0, 0, 0));

    public static TextUnit Unit(string key, int n, string? speaker, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, speaker, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    public static ProjectLines Lines(params (string Key, TextUnit[] Units)[] scripts) =>
        ProjectLines.Create(Texts(scripts));

    public static ProjectLines Lines(QaReport report, params (string Key, TextUnit[] Units)[] scripts) =>
        ProjectLines.Create(Texts(scripts), report);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts((string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);
}
