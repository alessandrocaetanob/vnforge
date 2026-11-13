using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.Core.Injection;

// Pasta do jogo que mudou de lugar (#689): o que a troca precisa ler e regravar nos backups. A conferência do jogo novo e a ordem das gravações ficam no ProjectGameRelocator.
public sealed partial class GameInjector
{
    /// <summary>A entrada mais nova de cada arquivo entre os backups ainda não restaurados, a mesma que <see cref="Plan"/> usa para reconhecer o build de uma injeção anterior.</summary>
    /// <exception cref="InjectionException">Um manifesto não restaurado está ilegível.</exception>
    internal static IReadOnlyDictionary<string, BackupEntry> LatestUnrestoredEntries(ProjectFolder project)
    {
        var latest = new Dictionary<string, BackupEntry>(PathComparer);
        foreach (var backup in ReadBackups(project))
        {
            foreach (var entry in backup.Manifest.Files)
            {
                latest.TryAdd(entry.Path, entry);
            }
        }

        return latest;
    }

    /// <summary>Algum manifesto legível (restaurado ou não) aponta para uma pasta que não é <paramref name="gameDirectory"/>.</summary>
    internal static bool AnyManifestElsewhere(ProjectFolder project, string gameDirectory) =>
        ManifestsElsewhere(project, gameDirectory).Any();

    /// <summary>
    /// Passa para <paramref name="gameDirectory"/> a pasta de jogo de todos os manifestos (restaurados ou não) que apontam para outra. O restore usa a pasta do manifesto quando ela ainda existe,
    /// então sem isso uma cópia do jogo deixada no lugar antigo seria restaurada no lugar do jogo novo, e o plano de restauração só junta backups da mesma pasta.
    /// Só mexe na pasta <c>backup/</c> do projeto, nunca no jogo. Manifesto ilegível, ou com a pasta vazia ou inválida, fica como está.
    /// </summary>
    /// <returns>Quantos manifestos foram regravados.</returns>
    internal static int RetargetManifests(ProjectFolder project, string gameDirectory)
    {
        var retargeted = 0;
        foreach (var (directory, manifest) in ManifestsElsewhere(project, gameDirectory).ToList())
        {
            WriteManifest(directory, manifest with { GameDirectory = gameDirectory });
            retargeted++;
        }

        return retargeted;
    }

    private static IEnumerable<(string Directory, BackupManifest Manifest)> ManifestsElsewhere(ProjectFolder project, string gameDirectory)
    {
        if (!Directory.Exists(project.BackupDirectory))
        {
            yield break;
        }

        var wanted = GameLibrary.NormalizeFolder(gameDirectory);
        foreach (var directory in Directory.EnumerateDirectories(project.BackupDirectory).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, BackupManifest.FileName);
            if (!File.Exists(manifestPath) || TryReadManifest(manifestPath).Manifest is not { } manifest)
            {
                continue;
            }

            string recorded;
            try
            {
                recorded = GameLibrary.NormalizeFolder(manifest.GameDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!string.Equals(recorded, wanted, PathComparison))
            {
                yield return (directory, manifest);
            }
        }
    }
}
