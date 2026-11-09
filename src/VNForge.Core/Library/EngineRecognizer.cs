using VNForge.Core.Engines;

namespace VNForge.Core.Library;

/// <summary>
/// Reconhece a engine de uma pasta de jogo. Primeiro as engines registradas (<see cref="IEngine.Detect"/>), que o app sabe traduzir; depois as assinaturas de
/// arquivo (<see cref="EngineSignatures"/>) das que ele ainda não traduz. Só lê.
/// </summary>
public sealed class EngineRecognizer(IReadOnlyList<IEngine> engines)
{
    /// <summary>A engine da pasta, ou nulo quando nada a reconhece. Pasta que a engine não consegue ler vale como não reconhecida por ela.</summary>
    public LibraryEngine? Recognize(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        foreach (var engine in engines)
        {
            try
            {
                var detection = engine.Detect(folder);
                if (detection.IsMatch)
                {
                    return new LibraryEngine(engine.Id, engine.DisplayName, LibraryEngineSource.Engine, string.IsNullOrWhiteSpace(detection.Summary) ? null : detection.Summary);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or VNForgeException)
            {
                // A engine não leu a pasta: não é jogo dela, e as outras ainda podem reconhecê-la.
            }
        }

        return EngineSignatures.Match(folder);
    }
}
