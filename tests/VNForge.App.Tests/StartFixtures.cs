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

    /// <summary>Grava um pacote em <c>build/</c> com a data pedida e, com <paramref name="replaced"/>, o <c>build-state.json</c> que o <c>vnf build</c> deixa.</summary>
    public static void AddBuild(string root, DateTimeOffset when, int? replaced)
    {
        var folder = ProjectFolder.Open(root);
        Directory.CreateDirectory(folder.BuildDirectory);
        var package = Path.Combine(folder.BuildDirectory, "ysbin.ypf");
        File.WriteAllBytes(package, [1, 2, 3]);
        File.SetLastWriteTimeUtc(package, when.UtcDateTime);
        if (replaced is { } count)
        {
            Directory.CreateDirectory(folder.CacheDirectory);
            File.WriteAllText(
                Path.Combine(folder.CacheDirectory, "build-state.json"),
                $$"""{"version":1,"scripts":{},"archives":[{"gamePath":"ysbin.ypf","replacedFiles":{{count}},"length":3}],"warnings":[]}""");
        }
    }
}
