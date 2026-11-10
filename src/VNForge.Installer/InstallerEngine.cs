using VNForge.Patching;

namespace VNForge.Installer;

/// <summary>
/// O que o instalador faz com o patch e com a pasta do jogo. As janelas e o modo de console falam com isto, não com o <see cref="PatchInstaller"/>,
/// para os testes trocarem o disco por um jogo de mentira que para onde o teste quer.
/// </summary>
public interface IInstallerEngine
{
    PatchManifest Manifest { get; }

    PatchCheck Check(string folder);

    PatchUninstallPlan PlanUninstall(string folder);

    PatchInstallResult Install(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken);

    PatchUninstallResult Uninstall(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken);
}

/// <summary>O <see cref="IInstallerEngine"/> de verdade: o patch embutido no executável, aplicado pelo <see cref="PatchInstaller"/> (regras 2 e 3 do CLAUDE.md).</summary>
public sealed class PatchEngine(PatchPackage package) : IInstallerEngine
{
    public PatchManifest Manifest => package.Manifest;

    public PatchCheck Check(string folder) => PatchInstaller.Check(folder, package);

    public PatchUninstallPlan PlanUninstall(string folder) => PatchInstaller.PlanUninstall(folder);

    public PatchInstallResult Install(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken) =>
        PatchInstaller.Install(folder, package, progress, cancellationToken);

    public PatchUninstallResult Uninstall(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken) =>
        PatchInstaller.Uninstall(folder, progress, cancellationToken);
}

/// <summary>Onde procurar o jogo quando o jogador não diz: a pasta onde abriu o instalador, a do instalador e a de cima dela.</summary>
public static class GameFolderLocator
{
    public static IReadOnlyList<string> Candidates(string currentDirectory, string? executableDirectory) =>
        new[] { currentDirectory, executableDirectory, executableDirectory is null ? null : Path.GetDirectoryName(executableDirectory) }
            .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
            .Select(d => d!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>A primeira pasta que não é de outra versão do jogo (pronta para instalar, ou já instalada).</summary>
    public static (string Folder, PatchCheck Check)? Find(IInstallerEngine engine, IEnumerable<string> candidates)
    {
        foreach (var folder in candidates)
        {
            try
            {
                var check = engine.Check(folder);
                if (check.State != PatchCheckState.WrongVersion)
                {
                    return (folder, check);
                }
            }
            catch (Exception ex) when (ex is PatchException or IOException or UnauthorizedAccessException)
            {
                // A pasta onde o jogador abriu o instalador pode ser a Área de Trabalho ou a de Downloads; uma que não dá para ler só não é a do jogo.
            }
        }

        return null;
    }
}
