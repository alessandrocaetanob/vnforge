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

    /// <summary>
    /// Passa para <paramref name="gameDirectory"/> a pasta de jogo de todos os manifestos (restaurados ou não) que apontam para outra. O restore usa a pasta do manifesto quando ela ainda existe,
    /// então sem isso uma cópia do jogo deixada no lugar antigo seria restaurada no lugar do jogo novo, e o plano de restauração só junta backups da mesma pasta.
    /// Só mexe na pasta <c>backup/</c> do projeto, nunca no jogo. Manifesto ilegível fica como está.
    /// </summary>
    /// <returns>Quantos manifestos foram regravados.</returns>
    internal static int RetargetManifests(ProjectFolder project, string gameDirectory)
    {
        if (!Directory.Exists(project.BackupDirectory))
        {
            return 0;
        }

        var retargeted = 0;
        foreach (var directory in Directory.EnumerateDirectories(project.BackupDirectory).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, BackupManifest.FileName);
            if (!File.Exists(manifestPath) || TryReadManifest(manifestPath).Manifest is not { } manifest)
            {
                continue;
            }

            if (!string.Equals(NormalizedFolder(manifest.GameDirectory), NormalizedFolder(gameDirectory), PathComparison))
            {
                WriteManifest(directory, manifest with { GameDirectory = gameDirectory });
                retargeted++;
            }
        }

        return retargeted;
    }

    private static string NormalizedFolder(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
