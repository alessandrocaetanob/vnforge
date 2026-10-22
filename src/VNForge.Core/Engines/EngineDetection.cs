namespace VNForge.Core.Engines;

/// <summary>
/// Resultado de <see cref="IEngine.Detect"/>: diz se a pasta é de um jogo da engine e o que foi reconhecido nela.
/// </summary>
/// <param name="IsMatch">Verdadeiro quando a pasta parece ser de um jogo desta engine.</param>
/// <param name="Summary">Resumo para o usuário, em português (ex.: "parece YU-RIS 5.x, ysbin.ypf v500"). Vazio quando não reconhece.</param>
/// <param name="Details">Pares nome/valor do diagnóstico (versões, arquivos de pacote, chaves), em ordem estável.</param>
public sealed record EngineDetection(
    bool IsMatch,
    string Summary,
    IReadOnlyDictionary<string, string> Details)
{
    /// <summary>Resultado para uma pasta que a engine não reconhece.</summary>
    public static EngineDetection NotDetected { get; } =
        new(false, string.Empty, new Dictionary<string, string>());

    /// <summary>Cria o resultado de uma pasta reconhecida.</summary>
    public static EngineDetection Matched(string summary, IReadOnlyDictionary<string, string>? details = null) =>
        new(true, summary, details ?? new Dictionary<string, string>());
}
