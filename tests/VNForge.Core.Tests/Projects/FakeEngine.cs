using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>Engine de mentira dos testes do build: os scripts são <c>.fake</c> com uma linha <c>tipo|texto</c> por unidade e o pacote é um arquivo só.</summary>
internal sealed class FakeEngine : IEngine, IGameUnpacker, IGameRepacker, IGamePatchProvider
{
    internal const string PackageName = "pacote.fake";

    private readonly FakeFormat _format = new();

    /// <summary>Os patches de executável que a engine oferece; vazio por padrão, como uma engine sem patch.</summary>
    public Func<string, IReadOnlyList<IGamePatch>> ExePatches { get; set; } = _ => [];

    public IReadOnlyList<IGamePatch> CreatePatches(string executable) => ExePatches(executable);

    public string Id => "fake";

    public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

    public IReadOnlyList<IArchiveFormat> Archives => [];

    public IReadOnlyList<IScriptFormat> Scripts => [_format];

    public IReadOnlyList<IGamePatch> Patches => [];

    public int RebuildCalls => _format.Calls;

    public IReadOnlyList<TextUnit> LastRebuiltUnits => _format.LastUnits;

    public IReadOnlyList<RebuiltScript> LastRebuilt { get; private set; } = [];

    public int RepackCalls { get; private set; }

    public VNForgeException? RepackFailure { get; set; }

    public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        var archive = Path.Combine(destinationDirectory, PackageName);
        Directory.CreateDirectory(archive);
        foreach (var file in Directory.EnumerateFiles(gameDir, "*.fake"))
        {
            File.Copy(file, Path.Combine(archive, Path.GetFileName(file)), overwrite: true);
        }

        return new GameUnpackResult([new UnpackedArchive(PackageName, PackageName, 2, 0)]);
    }

    public UnpackedGame Open(string unpackedDirectory) =>
        new(
            new FakeContext(),
            Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(f => new UnpackedScript(Path.GetFileName(f), f, _format, null))
                .ToList());

    public GameRepackResult Repack(string gameDir, string unpackedDirectory, IReadOnlyList<RebuiltScript> rebuilt, string outputDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RepackFailure is not null)
        {
            throw RepackFailure;
        }

        RepackCalls++;
        LastRebuilt = rebuilt;
        var contents = Directory.EnumerateFiles(Path.Combine(unpackedDirectory, PackageName))
            .Order(StringComparer.Ordinal)
            .Select(f => rebuilt.FirstOrDefault(r => r.Path == f)?.Content ?? File.ReadAllBytes(f))
            .Select(Encoding.UTF8.GetString);
        File.WriteAllText(Path.Combine(outputDirectory, PackageName), string.Join('\u0001', contents));
        return new GameRepackResult([new RepackedArchive(PackageName, rebuilt.Count)]);
    }
}

internal sealed class FakeContext() : EngineContext("fake");

internal sealed class FakeFormat : IScriptFormat
{
    public int Calls { get; private set; }

    /// <summary>As unidades da última chamada de <see cref="Rebuild"/>, como o build as entregou.</summary>
    public IReadOnlyList<TextUnit> LastUnits { get; private set; } = [];

    public string Id => "fake";

    public IReadOnlyList<string> FileExtensions => [".fake"];

    public ScriptExtraction Extract(ScriptFile file, EngineContext ctx)
    {
        var units = Encoding.UTF8.GetString(file.Content.Span).Split('\n').Select(line =>
        {
            var bar = line.IndexOf('|', StringComparison.Ordinal);
            return new TextUnit(file.UnitId(0), Enum.Parse<TextUnitKind>(line[..bar], ignoreCase: true), null, line[(bar + 1)..], null, TranslationStatus.Untranslated);
        }).Select((u, i) => u with { Id = file.UnitId(i) }).ToList();
        return new ScriptExtraction(units);
    }

    public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx)
    {
        Calls++;
        LastUnits = units;
        var lines = Encoding.UTF8.GetString(file.Content.Span).Split('\n');
        foreach (var unit in units.Where(u => u.NeedsRewrite))
        {
            var index = int.Parse(unit.Id[(unit.Id.IndexOf(':', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                _ = encoder.Encode(unit.Translation!);
            }
            catch (EncoderFallbackException e)
            {
                throw new EncoderFallbackException($"A tradução da unidade '{unit.Id}' não pode ser gravada: {e.Message}", e);
            }

            var bar = lines[index].IndexOf('|', StringComparison.Ordinal);
            lines[index] = lines[index][..(bar + 1)] + unit.Translation;
        }

        return Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }
}
