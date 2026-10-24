using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Demo;

/// <summary>
/// O estado da tela Build do artboard sobre o projeto de demonstração, em disco e sem arquivo de jogo comercial: uma pasta de jogo sintética
/// com um pacote e o executável, os 312 scripts de <c>.cache/game</c>, e duas injeções antigas que deixaram os backups de 17/10 e 22/10.
/// A engine é falsa: reconstrói de verdade só 14 scripts (os outros saem iguais ao original) e grava um pacote de ~4,2 MB.
/// </summary>
internal static class DemoBuild
{
    public const string Archive = "ysbin.ypf";
    public const int OriginalBytes = 4 * 1024 * 1024;
    public const int RebuiltBytes = 4_404_019;

    /// <summary>Os 14 scripts que o build desta demonstração reconstrói.</summary>
    public static readonly IReadOnlySet<string> Rebuilt = new HashSet<string>(StringComparer.Ordinal)
    {
        "yst00012", "yst00013", "yst00014", "yst00016", "yst00090", "yst00091", "yst00106", "yst00107",
        "yst00100", "yst00101", "yst00102", "yst00103", "yst00104", "yst00105",
    };

    /// <summary>Grava a pasta do projeto, a do jogo e os backups em <paramref name="root"/> e devolve a pasta do projeto.</summary>
    public static string Create(string root)
    {
        var game = Directory.CreateDirectory(Path.Combine(root, "jogo")).FullName;
        File.WriteAllBytes(Path.Combine(game, Archive), Payload(OriginalBytes, 1));
        File.WriteAllText(Path.Combine(game, "MinhaVN.exe"), "MZ");

        var projectRoot = DemoProject.Create(Path.Combine(root, "projeto"), game);
        var project = ProjectFolder.Open(projectRoot);
        var scripts = Directory.CreateDirectory(Path.Combine(project.GameCacheDirectory, "pkg")).FullName;
        for (var n = 1; n <= DemoProject.ScriptCount; n++)
        {
            File.WriteAllText(Path.Combine(scripts, $"yst{n:D5}.ybn"), $"script {n}");
        }

        // Duas injeções antigas: a pasta do jogo termina com o build da segunda, e cada backup guarda o original.
        Inject(project, new DateTimeOffset(2026, 10, 17, 18, 20, 0, TimeSpan.Zero), Payload(4_300_000, 2));
        Inject(project, new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), Payload(4_350_000, 3));
        return projectRoot;
    }

    private static void Inject(ProjectFolder project, DateTimeOffset when, byte[] build)
    {
        Directory.CreateDirectory(project.BuildDirectory);
        File.WriteAllBytes(Path.Combine(project.BuildDirectory, Archive), build);
        new GameInjector(new Clock(when)).Inject(GameInjector.Plan(project));
    }

    /// <summary>Bytes que mudam de uma semente para outra: dois pacotes nunca têm o mesmo hash.</summary>
    public static byte[] Payload(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

/// <summary>A engine falsa da demonstração: descompactada é a pasta <c>.cache/game</c> do projeto, o build grava um pacote do tamanho fixo.</summary>
internal sealed class DemoBuildEngine : IEngine, IGameUnpacker, IGameRepacker
{
    private readonly DemoBuildFormat format = new();

    public string Id => "yuris";

    public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

    public IReadOnlyList<IArchiveFormat> Archives => [];

    public IReadOnlyList<IScriptFormat> Scripts => [format];

    public IReadOnlyList<IGamePatch> Patches => [];

    public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default) => new([]);

    public UnpackedGame Open(string unpackedDirectory) =>
        new(new DemoBuildContext(), [.. Directory.EnumerateFiles(unpackedDirectory, "*.ybn", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f =>
            {
                var name = Path.GetFileName(f);
                return new UnpackedScript(name, f, format, DemoProject.Label(Path.GetFileNameWithoutExtension(name)) is { Length: > 0 } label ? label : null);
            })]);

    public GameRepackResult Repack(string gameDir, string unpackedDirectory, IReadOnlyList<RebuiltScript> rebuilt, string outputDirectory, CancellationToken cancellationToken = default)
    {
        File.WriteAllBytes(Path.Combine(outputDirectory, DemoBuild.Archive), DemoBuild.Payload(DemoBuild.RebuiltBytes, 4));
        return new GameRepackResult([new RepackedArchive(DemoBuild.Archive, rebuilt.Count)]);
    }

    private sealed class DemoBuildContext() : EngineContext("yuris");

    private sealed class DemoBuildFormat : IScriptFormat
    {
        public string Id => "demo";

        public IReadOnlyList<string> FileExtensions => [".ybn"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx) => new([]);

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx) =>
            DemoBuild.Rebuilt.Contains(file.Key) ? [.. file.Content.ToArray(), (byte)'!'] : file.Content.ToArray();
    }
}

/// <summary>O QA da demonstração: sem ocorrência até alguém ligar, e então os 2 erros e 37 avisos do artboard.</summary>
internal sealed class DemoQaRule : IQaRule
{
    public bool Enabled { get; set; }

    public string Id => "demo";

    public string Description => "Ocorrências do artboard.";

    public IEnumerable<QaIssue> Check(QaContext context) => Enabled ? DemoProject.Report.Issues : [];
}
