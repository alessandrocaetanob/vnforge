using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>Engine de mentira dos testes do build: os scripts são <c>.fake</c> com uma linha <c>tipo|texto</c> por unidade e o pacote é um arquivo só.</summary>
internal sealed class FakeEngine : IEngine, IGameUnpacker, IGameRepacker, IGamePatchProvider, IGameTitleSource
{
    internal const string PackageName = "pacote.fake";

    /// <summary>O arquivo do título, na pasta do jogo e dentro do pacote: o texto dele, em UTF-8, é o título.</summary>
    internal const string TitleFileName = "titulo.cfg";

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

    /// <summary>Nomes de script que a engine pede para reconstruir mesmo sem tradução (<see cref="IScriptFormat.RequiresRebuild"/>).</summary>
    public HashSet<string> ScriptsRequiringRebuild => _format.RequiringRebuild;

    public IReadOnlyList<TextUnit> LastRebuiltUnits => _format.LastUnits;

    /// <summary>O codificador que o build entregou à engine na última chamada.</summary>
    public ITextEncoder? LastEncoder => _format.LastEncoder;

    public IReadOnlyList<RebuiltScript> LastRebuilt { get; private set; } = [];

    public int RepackCalls { get; private set; }

    public VNForgeException? RepackFailure { get; set; }

    public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        var archive = Path.Combine(destinationDirectory, PackageName);
        Directory.CreateDirectory(archive);
        foreach (var file in Directory.EnumerateFiles(gameDir, "*.fake").Concat(Directory.EnumerateFiles(gameDir, TitleFileName)))
        {
            File.Copy(file, Path.Combine(archive, Path.GetFileName(file)), overwrite: true);
        }

        return new GameUnpackResult([new UnpackedArchive(PackageName, PackageName, 2, 0)]);
    }

    /// <summary>Quantas vezes o build pediu o título da pasta descompactada.</summary>
    public int TitleReads { get; private set; }

    /// <summary>Quando definido, <see cref="ReadTitle"/> lança: o arquivo do título está num formato que a engine não lê.</summary>
    public VNForgeException? TitleFailure { get; set; }

    public GameTitleFile? ReadTitle(string unpackedDirectory)
    {
        TitleReads++;
        if (TitleFailure is not null)
        {
            throw TitleFailure;
        }

        var path = Path.Combine(unpackedDirectory, PackageName, TitleFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return new GameTitleFile(path, bytes, Encoding.UTF8.GetString(bytes));
    }

    public byte[] WithTitle(GameTitleFile file, string title, ITextEncoder encoder)
    {
        _ = encoder.Encode(title);
        return Encoding.UTF8.GetBytes(title);
    }

    public UnpackedGame Open(string unpackedDirectory) =>
        new(
            new FakeContext(),
            Directory.EnumerateFiles(unpackedDirectory, "*.fake", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(f => new UnpackedScript(Path.GetFileName(f), f, _format, null))
                .ToList());

    public GameRepackResult Repack(string gameDir, string unpackedDirectory, IReadOnlyList<RebuiltScript> rebuilt, string outputDirectory, Func<string, string>? originalOf = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RepackFailure is not null)
        {
            throw RepackFailure;
        }

        RepackCalls++;
        LastRebuilt = rebuilt;
        var originals = Directory.EnumerateFiles(Path.Combine(unpackedDirectory, PackageName)).Order(StringComparer.Ordinal).ToList();
        // Como a engine de verdade: o pacote original lido (o do jogo, ou o que o build indicou) tem que ser o que a extração descompactou.
        var source = originalOf?.Invoke(PackageName) ?? Path.Combine(gameDir, PackageName);
        if (File.Exists(source) && File.ReadAllText(source) != string.Join('', originals.Select(f => Encoding.UTF8.GetString(File.ReadAllBytes(f)))))
        {
            throw new VNForgeException($"A entrada de {PackageName} não é mais a que o vnf extract descompactou: o jogo mudou depois da extração. Rode vnf extract de novo.");
        }

        var contents = originals
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

    public HashSet<string> RequiringRebuild { get; } = [];

    public bool RequiresRebuild(ScriptFile file, ITextEncoder encoder, EngineContext ctx)
    {
        LastEncoder = encoder;
        return RequiringRebuild.Contains(file.Name);
    }

    /// <summary>As unidades da última chamada de <see cref="Rebuild"/>, como o build as entregou.</summary>
    public IReadOnlyList<TextUnit> LastUnits { get; private set; } = [];

    /// <summary>O codificador da última chamada de <see cref="Rebuild"/> e de <see cref="RequiresRebuild"/>.</summary>
    public ITextEncoder? LastEncoder { get; private set; }

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
        LastEncoder = encoder;
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
