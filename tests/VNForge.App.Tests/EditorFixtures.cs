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

    /// <summary>Um script com <paramref name="count"/> linhas: status em rodízio, quatro falantes (um é a narração) e um aviso de QA a cada 7 linhas.</summary>
    public static ProjectLines Large(int count, string key = "yst00001")
    {
        string?[] speakers = [null, "美咲", "健", "Ana"];
        var units = new TextUnit[count];
        var issues = new List<QaIssue>();
        for (var i = 0; i < count; i++)
        {
            units[i] = Unit(key, i + 1, speakers[i % speakers.Length], Statuses[i % Statuses.Length]);
            if (i % 7 == 0)
            {
                issues.Add(new QaIssue("regra", QaSeverity.Warning, units[i].Id, key, "problema"));
            }
        }

        return Lines(new QaReport(issues, [], ["regra"]), (key, units));
    }

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts((string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);
}
