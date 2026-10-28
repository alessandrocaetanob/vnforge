using VNForge.App.Services;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

internal sealed class TempFolder : IDisposable
{
    public TempFolder() : this(string.Empty)
    {
    }

    public TempFolder(string prefix)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vnforge-app-tests", prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string SubFolder(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;

    public void Dispose()
    {
        // Os ViewModels leem o projeto em segundo plano (Editor, Glossário, estimativa dos Ajustes) e um teste pode terminar com uma leitura ainda
        // aberta. Esperar o arquivo ser liberado é legítimo; um handle que nunca fecha estoura o prazo e a IOException original sobe.
        var deadline = Environment.TickCount64 + 10_000;
        while (Directory.Exists(Path))
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(25);
            }
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

/// <summary>Engine que reconhece qualquer pasta, com versão e chave de mentira nos detalhes da detecção.</summary>
internal sealed class FakeEngine(bool matches = true) : IEngine
{
    public string Id => "yuris";

    public EngineDetection Detect(string gameDir) => matches
        ? EngineDetection.Matched("parece fake", new Dictionary<string, string>
        {
            ["ypfVersion"] = "500",
            ["scriptKey"] = "0xD36FAC96",
            ["scriptsArchive"] = "ysbin.ypf",
            ["scripts"] = "2",
        })
        : EngineDetection.NotDetected;

    public IReadOnlyList<IArchiveFormat> Archives => [];

    public IReadOnlyList<IScriptFormat> Scripts => [];

    public IReadOnlyList<IGamePatch> Patches => [];
}

/// <summary>Provedor de IA que reescreve com as opções dadas, ou recusa; a tradução comum não existe nele.</summary>
internal sealed class ReescritaFixa(bool refused, params string[] opcoes) : VNForge.AI.Translation.ITranslationProvider, VNForge.AI.Translation.IRewriteProvider
{
    public ReescritaFixa(params string[] opcoes)
        : this(false, opcoes)
    {
    }

    public Task<IReadOnlyList<VNForge.AI.Translation.TranslationResult>> TranslateAsync(VNForge.AI.Translation.TranslationBatch batch, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<VNForge.AI.Translation.RewriteResult> RewriteAsync(VNForge.AI.Translation.TranslationBatch batch, VNForge.AI.Translation.RewriteRequest request, CancellationToken ct) =>
        Task.FromResult(new VNForge.AI.Translation.RewriteResult(batch.Lines[0].Id, opcoes, refused));
}
