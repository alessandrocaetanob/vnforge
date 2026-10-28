using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Yscfg;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis;

/// <summary>A engine YU-RIS: pacotes YPF e scripts YSTB.</summary>
public sealed class YuRisEngine : IEngine, IGameUnpacker, IGameRepacker, IGamePatchProvider, IGameTitleSource
{
    /// <summary>Valor de <see cref="Id"/>, também o <see cref="EngineContext.EngineId"/> do <see cref="YuRisContext"/>.</summary>
    public const string EngineId = "yuris";

    private readonly YstbScriptFormat _scriptFormat = new();

    public YuRisEngine()
    {
        Scripts = [_scriptFormat];
    }

    public string Id => EngineId;

    public string DisplayName => "YU-RIS";

    public IReadOnlyList<IArchiveFormat> Archives { get; } = [new YpfArchiveFormat()];

    public IReadOnlyList<IScriptFormat> Scripts { get; }

    /// <summary>Vazia: os patches do YU-RIS dependem do executável do projeto e saem de <see cref="CreatePatches"/>.</summary>
    public IReadOnlyList<IGamePatch> Patches { get; } = [];

    /// <summary>O patch do nome da fonte (<see cref="YuRisFontPatch"/>) para o executável do projeto.</summary>
    public IReadOnlyList<IGamePatch> CreatePatches(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        return [new YuRisFontPatch(executable)];
    }

    /// <summary>
    /// Procura pacotes <c>.ypf</c> na pasta do jogo e na subpasta <c>pac</c>, só lendo. Reconhece a pasta quando algum
    /// arquivo começa com a assinatura YPF; no pacote de scripts (<c>ysbin.ypf</c> primeiro; vale o primeiro com
    /// <c>yst*.ybn</c>) informa a versão do YPF, a do YSTB e a chave XOR. Pasta inexistente, vazia ou de outra engine volta
    /// como não reconhecida. Arquivo ou pasta ilegível vira o detalhe <c>warnings</c>, também quando nada é reconhecido, nunca exceção.
    /// </summary>
    /// <param name="gameDir">Caminho da pasta do jogo.</param>
    public EngineDetection Detect(string gameDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        return YuRisDetector.Detect(gameDir);
    }

    /// <summary>
    /// Descompacta os pacotes com scripts (<c>yst*.ybn</c>), o <c>ysc.ybn</c> ou o <c>yst_list.ybn</c> para uma subpasta por pacote
    /// (<c>ysbin.ypf</c> vira <c>&lt;destino&gt;/ysbin.ypf/...</c>, com os caminhos internos). Os demais pacotes não são tocados.
    /// </summary>
    public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return YuRisUnpacker.Unpack(gameDir, destinationDirectory, cancellationToken);
    }

    /// <summary>
    /// Lê o <c>ysc.ybn</c> (obrigatório) e o <c>yst_list.ybn</c> (opcional) de qualquer subpasta de <paramref name="unpackedDirectory"/> e
    /// lista os <c>yst*.ybn</c>. O contexto devolvido é um <see cref="YuRisContext"/>.
    /// </summary>
    public UnpackedGame Open(string unpackedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unpackedDirectory);
        return YuRisUnpacker.Open(unpackedDirectory, _scriptFormat);
    }

    /// <summary>
    /// Lê o título da janela do <c>yscfg.ybn</c> que o <see cref="Unpack"/> deixou em qualquer subpasta de <paramref name="unpackedDirectory"/>
    /// (<c>ysbin.ypf/ysbin/yscfg.ybn</c> no jogo de referência). Pasta sem o arquivo devolve nulo.
    /// </summary>
    /// <exception cref="InvalidFileException">O <c>yscfg.ybn</c> não segue o formato.</exception>
    /// <exception cref="VNForgeException">Há mais de um <c>yscfg.ybn</c>, então não dá para saber qual vale.</exception>
    public GameTitleFile? ReadTitle(string unpackedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unpackedDirectory);
        if (!Directory.Exists(unpackedDirectory))
        {
            return null;
        }

        var found = Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(f), YuRisUnpacker.ConfigFile, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (found.Count == 0)
        {
            return null;
        }

        if (found.Count > 1)
        {
            throw new VNForgeException(
                $"Há {found.Count} arquivos {YuRisUnpacker.ConfigFile} em '{unpackedDirectory}' ({string.Join(", ", found.Select(f => Path.GetRelativePath(unpackedDirectory, f)))}), e não dá para saber qual traz o título da janela.");
        }

        var content = File.ReadAllBytes(found[0]);
        return new GameTitleFile(found[0], content, YscfgReader.Read(content, found[0]).Title);
    }

    /// <summary>
    /// O <c>yscfg.ybn</c> com o título trocado, codificado por <paramref name="encoder"/>; o cabeçalho e a configuração saem como estavam.
    /// </summary>
    /// <exception cref="InvalidFileException"><paramref name="file"/> não segue o formato.</exception>
    /// <exception cref="EncoderFallbackException">O <paramref name="encoder"/> não grava algum caractere.</exception>
    /// <exception cref="VNForgeException">O título passa do que o campo de tamanho do arquivo guarda.</exception>
    public byte[] WithTitle(GameTitleFile file, string title, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(encoder);

        var parsed = YscfgReader.Read(file.Content, file.Path);
        var encoded = encoder.Encode(title);
        try
        {
            return YscfgWriter.WithTitle(parsed, encoded);
        }
        catch (ArgumentException ex)
        {
            throw new VNForgeException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Grava em <paramref name="outputDirectory"/> os pacotes que <see cref="Unpack"/> descompactou, com os scripts reconstruídos no lugar
    /// dos originais e o resto byte a byte igual (<c>ysbin.ypf</c> vai para <c>&lt;destino&gt;/ysbin.ypf</c>). Pacote sem script alterado é
    /// copiado idêntico; num alterado, cada script confere antes que a entrada do YPF ainda é a que foi extraída.
    /// </summary>
    public GameRepackResult Repack(
        string gameDir,
        string unpackedDirectory,
        IReadOnlyList<RebuiltScript> rebuilt,
        string outputDirectory,
        Func<string, string>? originalOf = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(unpackedDirectory);
        ArgumentNullException.ThrowIfNull(rebuilt);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return YuRisRepacker.Repack(gameDir, unpackedDirectory, rebuilt, outputDirectory, originalOf, cancellationToken);
    }
}
