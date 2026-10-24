using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vnforge-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string SubFolder(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeFolderPicker(string? answer) : IFolderPicker
{
    public int Calls { get; private set; }

    public Task<string?> PickFolderAsync(string title)
    {
        Calls++;
        return Task.FromResult(answer);
    }
}

internal static class SampleProjects
{
    /// <summary>Cria um projeto como o <c>vnf init</c> e o <c>vnf extract</c> deixam, com as linhas nos status pedidos.</summary>
    public static string Create(string root, string name, params TranslationStatus[] statuses)
    {
        var folder = ProjectFolder.Create(root, new ProjectConfig(
            name,
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        var units = statuses
            .Select((s, i) => new TextUnit($"yst00001:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", s == TranslationStatus.Untranslated ? null : $"tradução {i}", s))
            .ToList();
        folder.SaveText("yst00001", units);
        return folder.Root;
    }
}
