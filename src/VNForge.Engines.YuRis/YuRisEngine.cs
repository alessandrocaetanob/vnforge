using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Engines.YuRis;

/// <summary>A engine YU-RIS: pacotes YPF e scripts YSTB.</summary>
public sealed class YuRisEngine : IEngine
{
    public string Id => "yuris";

    public IReadOnlyList<IArchiveFormat> Archives { get; } = [new YpfArchiveFormat()];

    public IReadOnlyList<IScriptFormat> Scripts { get; } = [];

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
}
