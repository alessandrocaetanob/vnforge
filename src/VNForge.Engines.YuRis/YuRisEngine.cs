using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis;

/// <summary>A engine YU-RIS: pacotes YPF e scripts YSTB.</summary>
public sealed class YuRisEngine : IEngine, IGameUnpacker
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
}
