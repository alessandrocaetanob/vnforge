using System.Globalization;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.Core.Injection;

/// <summary>Por que um backup à mão não leva os arquivos do jogo.</summary>
public enum ManualBackupGame
{
    /// <summary>Os originais que o build troca entram no backup.</summary>
    Included,

    /// <summary>A pasta do jogo do projeto não existe: entra só o projeto.</summary>
    NoGame,

    /// <summary>Não há build, então não se sabe quais arquivos do jogo ele troca: entra só o projeto.</summary>
    NoBuild,
}

/// <summary>O que <see cref="GameInjector.BackUpNow"/> vai copiar, calculado sem gravar nada.</summary>
/// <param name="BackupRoot">Pasta onde a pasta do backup será criada.</param>
/// <param name="Game">Os arquivos do build com o original de cada um; nulo quando <paramref name="GameState"/> não é <see cref="ManualBackupGame.Included"/>.</param>
/// <param name="GameBytes">Tamanho dos originais do jogo que vão para o backup.</param>
/// <param name="ProjectFiles">Arquivos do projeto que vão para a subpasta <c>projeto/</c>, caminhos completos.</param>
/// <param name="ProjectBytes">Tamanho dos arquivos do projeto.</param>
public sealed record ManualBackupPlan(
    string BackupRoot,
    ManualBackupGame GameState,
    InjectionPlan? Game,
    long GameBytes,
    IReadOnlyList<string> ProjectFiles,
    long ProjectBytes)
{
    public long TotalBytes => GameBytes + ProjectBytes;

    /// <summary>Quantos arquivos do jogo entram.</summary>
    public int GameFileCount => Game?.Files.Count(f => f.ReplacesExisting) ?? 0;
}

/// <param name="Stage">O que está sendo copiado.</param>
/// <param name="Done">Arquivos já copiados nesta etapa.</param>
/// <param name="Total">Arquivos desta etapa.</param>
/// <param name="File">O arquivo que começa agora.</param>
public sealed record ManualBackupProgress(ManualBackupStage Stage, int Done, int Total, string File);

public enum ManualBackupStage
{
    Game,
    Project,
}

/// <param name="BackupDirectory">A pasta do backup criada.</param>
public sealed record ManualBackupResult(string BackupDirectory, int GameFiles, long GameBytes, int ProjectFiles, long ProjectBytes);

// O backup que a pessoa pede (#693): os originais do jogo que o build troca mais o projeto, sem tocar no jogo.
public sealed partial class GameInjector
{
    private static readonly string[] ProjectFoldersLeftOut = ["build", ".cache", "backup"];

    /// <summary>
    /// Diz o que o backup à mão levaria, sem gravar nada. Sem jogo ligado ou sem build, o plano leva só o projeto. Com o jogo, vale a regra da injeção:
    /// se o jogo já está com o build de uma injeção anterior, o original vem do backup dessa injeção.
    /// </summary>
    /// <exception cref="InjectionException">O jogo mudou desde a extração, ou um backup anterior está ilegível: as mesmas recusas de <see cref="Plan"/>.</exception>
    public static ManualBackupPlan PlanBackup(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var gameDirectory = Path.GetFullPath(project.LoadConfig().Game.Path, project.Root);
        var state = ManualBackupGame.Included;
        InjectionPlan? game = null;
        if (!Directory.Exists(gameDirectory))
        {
            state = ManualBackupGame.NoGame;
        }
        else if (!Directory.Exists(project.BuildDirectory) || !Directory.EnumerateFiles(project.BuildDirectory, "*", SearchOption.AllDirectories).Any())
        {
            state = ManualBackupGame.NoBuild;
        }
        else
        {
            game = PlanCore(project, includeUnchanged: true);
        }

        var root = project.Backups.CurrentRoot(project);
        var projectFiles = ProjectFilesToBackUp(project);
        var gameBytes = game?.Files.Where(f => f.ReplacesExisting).Sum(f => SizeOf(f.OriginalSource!)) ?? 0;
        return new ManualBackupPlan(root, state, game, gameBytes, projectFiles, projectFiles.Sum(SizeOf));
    }

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InjectionException($"Não consegui ler '{path}' para calcular o tamanho do backup: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Cria <c>AAAA-MM-DD_HHmm/</c> na pasta dos backups com o original de cada arquivo do jogo que o build troca e uma cópia do projeto em <c>projeto/</c>
    /// (sem <c>build/</c>, <c>.cache/</c> nem <c>backup/</c>), conferindo o hash de cada cópia, e grava o manifesto por último. Não escreve na pasta do jogo.
    /// Se algo falha ou a pessoa cancela, a pasta do backup é apagada: um backup pela metade não fica na lista.
    /// </summary>
    /// <exception cref="InjectionException">Pasta dos backups inacessível ou sem espaço (<see cref="InjectionException.FolderProblem"/>), cópia que não confere, ou jogo que mudou durante a cópia.</exception>
    /// <exception cref="OperationCanceledException">A pessoa cancelou; nada ficou gravado.</exception>
    public ManualBackupResult BackUpNow(ProjectFolder project, ManualBackupPlan plan, IProgress<ManualBackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);

        EnsureFolderAvailable(project, plan);
        var backupDirectory = CreateManualBackupDirectory(project, plan.BackupRoot);
        try
        {
            if (plan.Game is { } game)
            {
                BackUpOriginals(game, backupDirectory, (done, total, file) => progress?.Report(new ManualBackupProgress(ManualBackupStage.Game, done, total, file.RelativePath)), cancellationToken);
            }

            CopyProject(project, plan, backupDirectory, progress, cancellationToken);
            var gameDirectory = plan.Game?.GameDirectory ?? Path.GetFullPath(project.LoadConfig().Game.Path, project.Root);
            var entries = plan.Game?.Files.Where(f => f.ReplacesExisting)
                .Select(f => new BackupEntry(f.RelativePath, true, f.OriginalSha256, f.TargetSha256 ?? f.BuildSha256)).ToList() ?? [];
            WriteManifest(backupDirectory, new BackupManifest(BackupManifest.CurrentVersion, _timeProvider.GetUtcNow(), gameDirectory, entries, null, BackupManifest.ManualKind));
            ReleaseClaim(backupDirectory);
        }
        catch
        {
            TryDeleteDirectory(backupDirectory);
            throw;
        }

        return new ManualBackupResult(backupDirectory, plan.GameFileCount, plan.GameBytes, plan.ProjectFiles.Count, plan.ProjectBytes);
    }

    private void CopyProject(ProjectFolder project, ManualBackupPlan plan, string backupDirectory, IProgress<ManualBackupProgress>? progress, CancellationToken cancellationToken)
    {
        var target = Path.Combine(backupDirectory, BackupManifest.ProjectFolderName);
        for (var i = 0; i < plan.ProjectFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = plan.ProjectFiles[i];
            var relative = Path.GetRelativePath(project.Root, source);
            progress?.Report(new ManualBackupProgress(ManualBackupStage.Project, i, plan.ProjectFiles.Count, relative.Replace('\\', '/')));
            var copy = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            _fileSystem.CopyFile(source, copy);
        }
    }

    /// <summary>Todos os arquivos do projeto menos <c>build/</c>, <c>.cache/</c>, <c>backup/</c> e qualquer pasta de backup que esteja dentro do projeto.</summary>
    private static List<string> ProjectFilesToBackUp(ProjectFolder project)
    {
        var skipped = project.Backups.Roots(project).Select(GameLibrary.NormalizeFolder).ToList();
        var files = new List<string>();
        Walk(project.Root, isTop: true);
        return files;

        void Walk(string directory, bool isTop)
        {
            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                files.Add(file);
            }

            foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(child);
                if ((isTop && ProjectFoldersLeftOut.Contains(name, StringComparer.OrdinalIgnoreCase))
                    || skipped.Any(s => string.Equals(s, GameLibrary.NormalizeFolder(child), PathComparison)))
                {
                    continue;
                }

                Walk(child, isTop: false);
            }
        }
    }

    private static void EnsureFolderAvailable(ProjectFolder project, ManualBackupPlan plan)
    {
        var root = plan.BackupRoot;
        var missing = project.Backups.Unreachable(project).Any(m => string.Equals(m, root, PathComparison));
        if (missing)
        {
            throw Unavailable(project.Backups.Folder!, null);
        }

        var free = FreeSpace(root);
        if (free is { } available && available < plan.TotalBytes)
        {
            var drive = Path.GetPathRoot(root)!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            throw new InjectionException(
                $"Falta espaço em {drive}. O backup precisa de {Megabytes(plan.TotalBytes)} e o disco tem {Megabytes(available)} livres. Libere espaço ou escolha outra pasta nos Ajustes.")
            {
                FolderProblem = BackupFolderProblem.NotEnoughSpace,
            };
        }
    }

    private static InjectionException Unavailable(string folder, Exception? cause) =>
        new($"Não deu para gravar em {folder}. O disco não está conectado ou a pasta está protegida. Escolha outra pasta nos Ajustes. Nada foi alterado no jogo.", cause)
        {
            FolderProblem = BackupFolderProblem.Unavailable,
        };

    /// <summary>Espaço livre no disco da pasta, ou nulo quando o sistema não diz (caminho de rede, por exemplo): sem a medida, a cópia segue e falha sozinha se faltar.</summary>
    private static long? FreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string CreateManualBackupDirectory(ProjectFolder project, string backupRoot)
    {
        try
        {
            return CreateBackupDirectory(backupRoot);
        }
        catch (InjectionException ex) when (ex.FolderProblem == BackupFolderProblem.None)
        {
            throw Unavailable(project.Backups.Folder ?? backupRoot, ex);
        }
    }

    /// <summary>O tamanho como a tela e a CLI o escrevem: <c>463,7 MB</c>.</summary>
    public static string Megabytes(long bytes) => string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"{bytes / 1024.0 / 1024.0:N1} MB");
}
