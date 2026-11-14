using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Projetos sintéticos para os testes do Início: motor, modelo, texto e build escolhidos por quem chama.</summary>
internal static class StartFixtures
{
    public static string Create(string root, string name, string engineId = "yuris", string model = "claude-opus-5-5", params (string Key, TranslationStatus Status)[] lines)
    {
        var folder = ProjectFolder.Create(root, new ProjectConfig(
            name,
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig(engineId, engineId == "yuris" ? 500 : null, engineId == "yuris" ? "0xD36FAC96" : null),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default with { Model = model }));
        foreach (var script in lines.GroupBy(l => l.Key, StringComparer.Ordinal))
        {
            folder.SaveText(script.Key, [.. script.Select((l, i) => new TextUnit(
                $"{script.Key}:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", l.Status == TranslationStatus.Untranslated ? null : $"tradução {i}", l.Status))]);
        }

        return folder.Root;
    }
}
