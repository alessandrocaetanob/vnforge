using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis;

/// <summary>A engine YU-RIS: pacotes YPF e scripts YSTB.</summary>
public sealed class YuRisEngine : IEngine, IGameUnpacker, IGameRepacker
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

    public IReadOnlyList<IGamePatch> Patches { get; } = [];

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
    /// Grava em <paramref name="outputDirectory"/> os pacotes que <see cref="Unpack"/> descompactou, com os scripts reconstruídos no lugar
    /// dos originais e o resto byte a byte igual (<c>ysbin.ypf</c> vai para <c>&lt;destino&gt;/ysbin.ypf</c>). Pacote sem script alterado é
    /// copiado idêntico; num alterado, cada script confere antes que a entrada do YPF ainda é a que foi extraída.
    /// </summary>
    public GameRepackResult Repack(
        string gameDir,
        string unpackedDirectory,
        IReadOnlyList<RebuiltScript> rebuilt,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(unpackedDirectory);
        ArgumentNullException.ThrowIfNull(rebuilt);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return YuRisRepacker.Repack(gameDir, unpackedDirectory, rebuilt, outputDirectory, cancellationToken);
    }
}
