namespace VNForge.Core.Engines;

/// <summary>
/// Quem oferece patches que dependem do executável do projeto (<c>game.exe</c>), que só o projeto conhece. As engines implementam esta
/// interface além de <see cref="IEngine"/>; quem não a implementa não tem patch de executável. Os patches não dependem do projeto
/// continuam em <see cref="IEngine.Patches"/>.
/// </summary>
public interface IGamePatchProvider
{
    /// <summary>Patches desta engine para o executável <paramref name="executable"/>, que o fluxo de injeção aplica com backup.</summary>
    /// <param name="executable">Caminho do executável relativo à pasta do jogo (<c>game.exe</c> do <c>vnforge.json</c>).</param>
    IReadOnlyList<IGamePatch> CreatePatches(string executable);
}
