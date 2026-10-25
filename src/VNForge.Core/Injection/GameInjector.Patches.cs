using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Injection;

// Patches de arquivo do jogo (E5.5): o patch é aplicado numa cópia de trabalho e a cópia segue pelo mesmo caminho do build,
// com backup do original, manifesto, conferência de hash e restauração pelo vnf restore.
public sealed partial class GameInjector
{
    /// <summary>
    /// Confere se o <paramref name="patch"/> serve para o jogo e o aplica numa cópia de trabalho em <c>.cache/patch/&lt;id&gt;</c>, sem tocar no jogo.
    /// O plano traz só os arquivos que a cópia altera; um jogo que já está com o patch dá um plano sem arquivos.
    /// </summary>
    /// <exception cref="InjectionException">
    /// Pasta do jogo inexistente, caminho do patch fora da pasta, patch recusado por <see cref="IGamePatch.CanApply"/> (por exemplo, o executável
    /// não é o esperado), backup anterior ilegível ou falha ao preparar a cópia. Nada é gravado no jogo.
    /// </exception>
    public static PatchPlan PlanPatch(ProjectFolder project, IGamePatch patch)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(patch);

        var gameDirectory = ResolveGameDirectory(project);
        var relatives = patch.AffectedFiles.Select(f => Path.GetRelativePath(gameDirectory, ResolveInside(gameDirectory, f)).Replace('\\', '/')).ToList();
        if (relatives.Count == 0)
        {
            throw new InjectionException($"O patch '{patch.Id}' não declara nenhum arquivo para alterar.");
        }

        if (!patch.CanApply(gameDirectory, out var reason))
        {
            throw new InjectionException($"O patch '{patch.Id}' foi recusado e nada foi gravado no jogo: {reason}");
        }

        var staging = Path.Combine(project.CacheDirectory, "patch", patch.Id);
        try
        {
            var history = new BackupHistory(ReadBackups(project));
            ResetDirectory(staging);
            var targetHashes = new Dictionary<string, string>(PathComparer);
            foreach (var relative in relatives.Where(r => File.Exists(ResolveInside(gameDirectory, r))))
            {
                // O hash do jogo vem antes da cópia e a cópia é conferida contra ele: se o arquivo mudou no meio, o plano não grava um patch feito sobre a versão antiga.
                var target = ResolveInside(gameDirectory, relative);
                var targetHash = FileHash.Sha256(target);
                var stagedPath = ResolveInside(staging, relative);
                CopyInto(target, stagedPath);
                if (FileHash.Sha256(stagedPath) != targetHash)
                {
                    throw new InjectionException($"O arquivo '{relative}' do jogo mudou enquanto o patch '{patch.Id}' era preparado. Nada foi gravado no jogo; rode o comando de novo.");
                }

                targetHashes[relative] = targetHash;
            }

            patch.Apply(staging);
            return new PatchPlan(patch, PlanStaged(project, gameDirectory, staging, relatives, targetHashes, history), staging);
        }
        catch (InvalidOperationException ex)
        {
            TryDeleteDirectory(staging);
            throw new InjectionException($"O patch '{patch.Id}' foi recusado e nada foi gravado no jogo: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(staging);
            throw new InjectionException($"Não foi possível preparar o patch '{patch.Id}': {ex.Message} Nada foi gravado no jogo.", ex);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>Faz o backup do original, grava o arquivo alterado no jogo e confere o hash, como <see cref="Inject"/>; depois apaga a cópia de trabalho.</summary>
    /// <exception cref="InjectionException">As mesmas de <see cref="Inject"/>: o arquivo do jogo mudou depois do plano, backup que não confere ou gravação que falhou.</exception>
    public InjectionResult InjectPatch(PatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        try
        {
            return Inject(plan.Injection);
        }
        finally
        {
            TryDeleteDirectory(plan.StagingDirectory);
        }
    }

    /// <summary>Apaga a cópia de trabalho de um plano que não vai ser aplicado (o usuário recusou a confirmação).</summary>
    public static void DiscardPatch(PatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        TryDeleteDirectory(plan.StagingDirectory);
    }

    private static InjectionPlan PlanStaged(
        ProjectFolder project,
        string gameDirectory,
        string staging,
        IReadOnlyList<string> relatives,
        IReadOnlyDictionary<string, string> targetHashes,
        BackupHistory history)
    {
        var files = new List<PlannedFile>();
        var unchanged = 0;
        foreach (var relative in relatives)
        {
            var stagedPath = ResolveInside(staging, relative);
            if (!File.Exists(stagedPath))
            {
                continue;
            }

            var target = ResolveInside(gameDirectory, relative);
            var stagedHash = FileHash.Sha256(stagedPath);
            if (!targetHashes.TryGetValue(relative, out var targetHash))
            {
                files.Add(new PlannedFile(relative, stagedPath, target, stagedHash, null, null, null));
                continue;
            }

            if (targetHash == stagedHash)
            {
                unchanged++;
                continue;
            }

            files.Add(PlanReplacement(relative, stagedPath, target, stagedHash, targetHash, history));
        }

        return new InjectionPlan(gameDirectory, project.BackupDirectory, files, unchanged);
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void CopyInto(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }
}
