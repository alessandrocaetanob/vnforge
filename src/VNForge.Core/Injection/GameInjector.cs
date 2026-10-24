using System.Globalization;
using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Injection;

/// <summary>
/// Injeta o <c>build/</c> do projeto na pasta do jogo e devolve os originais. Cada arquivo de <c>build/</c> substitui o de mesmo
/// caminho relativo na pasta do jogo.
/// </summary>
/// <remarks>
/// Ordem da injeção: copia para <c>backup/AAAA-MM-DD_HHmm/</c> só os originais que serão trocados, confere o SHA-256 de cada cópia,
/// grava o <see cref="BackupManifest"/> e só então escreve no jogo, conferindo o hash de cada arquivo gravado. Se uma gravação falha,
/// os arquivos já trocados são devolvidos na hora; se nem isso é possível, o manifesto já existe e <c>vnf restore</c> termina o serviço.
/// Quando o jogo ainda está com o build de uma injeção anterior (sem restore no meio), o original vem do backup anterior,
/// para que cada backup guarde sempre os arquivos originais e nunca uma versão já traduzida.
/// </remarks>
public sealed class GameInjector
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private const string StampFormat = "yyyy-MM-dd_HHmm";

    private readonly TimeProvider _timeProvider;
    private readonly IInjectionFileSystem _fileSystem;

    public GameInjector(TimeProvider? timeProvider = null, IInjectionFileSystem? fileSystem = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileSystem = fileSystem ?? new PhysicalInjectionFileSystem();
    }

    /// <summary>Compara <c>build/</c> com a pasta do jogo e diz o que seria gravado. Não escreve nada.</summary>
    /// <exception cref="InjectionException"><c>build/</c> ausente ou vazia, pasta do jogo inexistente, ou backup anterior ilegível.</exception>
    public static InjectionPlan Plan(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var gameDirectory = ResolveGameDirectory(project);
        if (!Directory.Exists(project.BuildDirectory))
        {
            throw new InjectionException($"A pasta build/ não existe em '{project.Root}'. Rode vnf build antes de injetar.");
        }

        var buildFiles = Directory.EnumerateFiles(project.BuildDirectory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (buildFiles.Count == 0)
        {
            throw new InjectionException($"A pasta build/ está vazia em '{project.Root}'. Rode vnf build antes de injetar.");
        }

        var history = new BackupHistory(ReadBackups(project));
        EnsureGameUnchangedSinceExtraction(project, gameDirectory, history);
        var files = new List<PlannedFile>();
        var unchanged = 0;
        foreach (var buildPath in buildFiles)
        {
            var relative = Path.GetRelativePath(project.BuildDirectory, buildPath).Replace('\\', '/');
            if (string.Equals(relative, BackupManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InjectionException($"O nome '{BackupManifest.FileName}' é reservado ao manifesto do backup; renomeie o arquivo em build/.");
            }

            var target = ResolveInside(gameDirectory, relative);
            var buildHash = FileHash.Sha256(buildPath);
            if (!File.Exists(target))
            {
                files.Add(new PlannedFile(relative, buildPath, target, buildHash, null, null, null));
                continue;
            }

            var targetHash = FileHash.Sha256(target);
            if (targetHash == buildHash)
            {
                unchanged++;
                continue;
            }

            files.Add(PlanReplacement(relative, buildPath, target, buildHash, targetHash, history));
        }

        return new InjectionPlan(gameDirectory, project.BackupDirectory, files, unchanged);
    }

    /// <summary>Faz o backup, confere as cópias e grava o build no jogo. Sem arquivos no plano, não cria backup nem toca no jogo.</summary>
    /// <exception cref="InjectionException">
    /// Backup que não confere (nada foi gravado no jogo) ou gravação que falhou. A mensagem diz se o jogo voltou ao estado original
    /// ou se é preciso rodar <c>vnf restore</c>.
    /// </exception>
    public InjectionResult Inject(InjectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Files.Count == 0)
        {
            return new InjectionResult(plan.GameDirectory, null, [], plan.UnchangedCount);
        }

        var backupDirectory = CreateBackupDirectory(plan.BackupRoot);
        BackupManifest manifest;
        try
        {
            BackUpOriginals(plan, backupDirectory);
            manifest = new BackupManifest(
                BackupManifest.CurrentVersion,
                _timeProvider.GetUtcNow(),
                plan.GameDirectory,
                plan.Files.Select(f => new BackupEntry(f.RelativePath, f.ReplacesExisting, f.OriginalSha256, f.BuildSha256)).ToList());
            WriteManifest(backupDirectory, manifest);
        }
        catch (Exception ex) when (ex is InjectionException or IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(backupDirectory);
            throw ex as InjectionException
                ?? new InjectionException($"Não foi possível fazer o backup dos originais: {ex.Message} Nada foi gravado no jogo.", ex);
        }

        var written = new List<string>();
        foreach (var file in plan.Files)
        {
            try
            {
                written.Add(file.RelativePath);
                _fileSystem.CopyFile(file.BuildPath, file.TargetPath);
                VerifyHash(file.TargetPath, file.BuildSha256, file.RelativePath);
            }
            catch (Exception ex) when (ex is InjectionException or IOException or UnauthorizedAccessException)
            {
                throw RollBack(plan.GameDirectory, backupDirectory, manifest, written, file.RelativePath, ex);
            }
        }

        return new InjectionResult(plan.GameDirectory, backupDirectory, written, plan.UnchangedCount);
    }

    /// <summary>
    /// Monta o que a restauração devolve, sem escrever nada: o backup mais recente e, para os arquivos que só um backup mais velho guardou
    /// (uma injeção parcial depois dele), a entrada mais nova que mexeu em cada um. Só entram backups da mesma pasta de jogo.
    /// </summary>
    /// <exception cref="InjectionException">Não há backup, ou um manifesto está ilegível.</exception>
    public static RestorePlan PlanRestore(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var backups = ReadBackupsTolerant(project).ToList();
        var first = backups.FirstOrDefault()
            ?? throw new InjectionException($"Não há backup para restaurar em '{project.BackupDirectory}'. Só dá para restaurar depois de um vnf inject, e cada backup é restaurado uma vez.");
        if (first.Problem is not null)
        {
            throw first.Problem;
        }

        var latest = new BackupRecord(first.Directory, first.Manifest!);
        var gameDirectory = Directory.Exists(latest.Manifest.GameDirectory)
            ? latest.Manifest.GameDirectory
            : ResolveGameDirectory(project);

        var newest = new Dictionary<string, (string Directory, BackupEntry Entry)>(PathComparer);
        var consumed = new List<string>();
        var unreadable = new List<FoundBackup>();
        foreach (var backup in backups)
        {
            if (backup.Manifest is null)
            {
                unreadable.Add(backup);
                continue;
            }

            if (!string.Equals(backup.Manifest.GameDirectory, latest.Manifest.GameDirectory, PathComparison))
            {
                continue;
            }

            consumed.Add(backup.Directory);
            foreach (var entry in backup.Manifest.Files)
            {
                newest.TryAdd(entry.Path, (backup.Directory, entry));
            }
        }

        var warnings = unreadable.Select(u => SkipUnreadableBackup(u, newest)).ToList();
        var files = newest.Values.Select(v => v.Entry).OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
        var directories = newest.ToDictionary(p => p.Key, p => p.Value.Directory, PathComparer);
        return new RestorePlan(gameDirectory, latest.Directory, latest.Manifest with { Files = files }, directories)
        {
            ConsumedBackups = consumed,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Um backup antigo sem manifesto legível só pode ser pulado se nenhuma cópia dentro dele fica sem outra fonte: o que só ele guarda não
    /// voltaria ao jogo. Devolve o aviso do que foi pulado.
    /// </summary>
    private static string SkipUnreadableBackup(FoundBackup backup, Dictionary<string, (string Directory, BackupEntry Entry)> covered)
    {
        var needed = Directory.EnumerateFiles(backup.Directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(backup.Directory, f).Replace('\\', '/'))
            .Where(p => !string.Equals(p, BackupManifest.FileName, PathComparison) && !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Where(p => !covered.ContainsKey(p))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (needed.Count > 0)
        {
            throw new InjectionException(
                $"{backup.Problem!.Message} Esse backup guarda '{string.Join("', '", needed)}', que nenhum outro backup cobre, então a restauração foi recusada e nada foi gravado. "
                + "Restaure o manifesto a partir de uma cópia, ou devolva esses arquivos à mão a partir de " + $"'{backup.Directory}'.");
        }

        return $"{backup.Problem!.Message} Foi pulado: os arquivos dele têm outra fonte nos backups mais novos.";
    }

    /// <summary>
    /// Devolve ao jogo os originais do backup (e apaga os arquivos que a injeção criou). Confere todas as cópias do backup e o estado atual
    /// do jogo antes de escrever, e o hash de cada arquivo depois de escrever. Pode ser repetido: restaurar de novo dá o mesmo resultado.
    /// </summary>
    /// <exception cref="InjectionException">
    /// Cópia do backup ausente ou alterada, ou arquivo do jogo que não é nem o injetado nem o original (o jogo mudou depois da injeção):
    /// nada foi gravado. Ou falha ao gravar algum arquivo.
    /// </exception>
    public RestoreResult Restore(RestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string DirectoryOf(BackupEntry entry) =>
            plan.BackupDirectories.TryGetValue(entry.Path, out var directory) ? directory : plan.BackupDirectory;

        var problems = plan.Manifest.Files
            .Where(e => e.Existed)
            .Select(e => CheckBackupCopy(DirectoryOf(e), e))
            .OfType<string>()
            .ToList();
        if (problems.Count > 0)
        {
            throw new InjectionException($"O backup em '{plan.BackupDirectory}' não confere, e nada foi gravado no jogo: {string.Join(" ", problems)}");
        }

        var changed = FilesChangedSinceInjection(plan);
        if (changed.Count > 0)
        {
            throw new InjectionException(
                $"O jogo mudou depois da injeção (uma atualização, talvez), então a restauração foi recusada e nada foi gravado. Arquivos alterados: {string.Join(", ", changed)}. "
                + $"Restaurar agora desfaria essa mudança com os originais antigos. Se não precisa mais do backup, apague as pastas dele em '{Path.GetDirectoryName(plan.BackupDirectory)}'.");
        }

        var outcome = RestoreEntries(plan.GameDirectory, DirectoryOf, plan.Manifest.Files);
        if (outcome.Failures.Count > 0)
        {
            throw new InjectionException(
                $"Não foi possível restaurar tudo: {string.Join(" ", outcome.Failures)} O backup continua em '{plan.BackupDirectory}'; rode vnf restore de novo.");
        }

        var warnings = plan.Warnings.ToList();
        foreach (var directory in plan.ConsumedBackups)
        {
            try
            {
                MarkRestored(directory);
            }
            catch (Exception ex) when (ex is InjectionException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"O jogo foi restaurado, mas não deu para marcar o backup '{directory}' como restaurado: {ex.Message} Ele pode aparecer de novo na próxima restauração.");
            }
        }

        return new RestoreResult(plan.GameDirectory, plan.BackupDirectory, outcome.Restored, outcome.Removed, outcome.Kept) { Warnings = warnings };
    }

    /// <summary>Grava <see cref="BackupManifest.RestoredAt"/> no manifesto: o backup já devolveu os originais e não deve bloquear restaurações nem injeções futuras.</summary>
    private void MarkRestored(string backupDirectory)
    {
        var manifestPath = Path.Combine(backupDirectory, BackupManifest.FileName);
        var (manifest, problem) = TryReadManifest(manifestPath);
        if (manifest is null)
        {
            throw problem!;
        }

        WriteManifest(backupDirectory, manifest with { RestoredAt = _timeProvider.GetUtcNow() });
    }

    /// <summary>Arquivos do backup que existem no jogo com um conteúdo que não é o injetado nem o original: o jogo mudou depois da injeção.</summary>
    private static List<string> FilesChangedSinceInjection(RestorePlan plan)
    {
        var changed = new List<string>();
        foreach (var entry in plan.Manifest.Files.Where(e => e.Existed))
        {
            var target = ResolveInside(plan.GameDirectory, entry.Path);
            if (!File.Exists(target))
            {
                continue;
            }

            string current;
            try
            {
                current = FileHash.Sha256(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InjectionException($"Não foi possível ler '{entry.Path}' para conferir o jogo (o jogo está aberto?): {ex.Message}", ex);
            }

            if (current != entry.InjectedSha256 && current != entry.OriginalSha256)
            {
                changed.Add(entry.Path);
            }
        }

        return changed;
    }

    /// <summary>
    /// Recusa o plano quando um arquivo que a extração leu não tem mais o hash gravado em <c>.cache/state.json</c> (o jogo foi atualizado).
    /// O build de uma injeção anterior nossa não conta como mudança. Sem estado de extração, não há o que comparar.
    /// </summary>
    private static void EnsureGameUnchangedSinceExtraction(ProjectFolder project, string gameDirectory, BackupHistory history)
    {
        var changed = new List<string>();
        foreach (var original in project.LoadState().Originals.OrderBy(o => o.Path, StringComparer.Ordinal))
        {
            var path = ResolveInside(gameDirectory, original.Path);
            if (!File.Exists(path))
            {
                changed.Add($"{original.Path} (ausente)");
                continue;
            }

            string current;
            try
            {
                current = FileHash.Sha256(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InjectionException($"Não foi possível ler '{original.Path}' para conferir o jogo (o jogo está aberto?): {ex.Message}", ex);
            }

            if (current == original.Sha256 || history.FindLatestEntry(original.Path)?.Entry.InjectedSha256 == current)
            {
                continue;
            }

            changed.Add(original.Path);
        }

        if (changed.Count > 0)
        {
            throw new InjectionException(
                $"O jogo mudou desde a extração (uma atualização, talvez), então a injeção foi recusada e nada foi gravado. Arquivos alterados: {string.Join(", ", changed)}. "
                + "Rode vnf extract de novo para o projeto acompanhar a nova versão do jogo.");
        }
    }

    private static PlannedFile PlanReplacement(
        string relative,
        string buildPath,
        string target,
        string buildHash,
        string targetHash,
        BackupHistory history)
    {
        var earlier = history.FindLatestEntry(relative);
        if (earlier is not null && earlier.Entry.InjectedSha256 == targetHash)
        {
            // O jogo está com o build de uma injeção anterior: o original de verdade está nesse backup.
            return earlier.Entry.Existed
                ? new PlannedFile(relative, buildPath, target, buildHash, earlier.Entry.OriginalSha256, ResolveInside(earlier.Directory, relative), targetHash)
                : new PlannedFile(relative, buildPath, target, buildHash, null, null, targetHash);
        }

        return new PlannedFile(relative, buildPath, target, buildHash, targetHash, target, targetHash);
    }

    private void BackUpOriginals(InjectionPlan plan, string backupDirectory)
    {
        foreach (var file in plan.Files)
        {
            CheckTargetUnchanged(file);
        }

        foreach (var file in plan.Files.Where(f => f.ReplacesExisting))
        {
            var copy = ResolveInside(backupDirectory, file.RelativePath);
            _fileSystem.CopyFile(file.OriginalSource!, copy);
            var hash = FileHash.Sha256(copy);
            if (hash != file.OriginalSha256)
            {
                throw new InjectionException(
                    $"A cópia de backup de '{file.RelativePath}' não confere com o original (o jogo pode ter mudado durante a operação). Nada foi gravado no jogo.");
            }
        }
    }

    private static void CheckTargetUnchanged(PlannedFile file)
    {
        var unchanged = file.TargetSha256 is null
            ? !File.Exists(file.TargetPath)
            : File.Exists(file.TargetPath) && FileHash.Sha256(file.TargetPath) == file.TargetSha256;
        if (!unchanged)
        {
            throw new InjectionException(
                $"O arquivo '{file.RelativePath}' do jogo mudou depois da conferência (o jogo pode ter sido atualizado durante a operação). Nada foi gravado no jogo.");
        }
    }

    private InjectionException RollBack(
        string gameDirectory,
        string backupDirectory,
        BackupManifest manifest,
        List<string> attempted,
        string failedFile,
        Exception cause)
    {
        var entries = manifest.Files.Where(e => attempted.Contains(e.Path)).ToList();
        var outcome = RestoreEntries(gameDirectory, _ => backupDirectory, entries, removeModifiedCreations: true);
        if (outcome.Failures.Count == 0)
        {
            return new InjectionException(
                $"A injeção falhou em '{failedFile}': {cause.Message} O jogo voltou ao estado original; o backup está em '{backupDirectory}'.",
                cause);
        }

        return new InjectionException(
            $"A injeção falhou em '{failedFile}': {cause.Message} Não foi possível desfazer tudo ({string.Join(" ", outcome.Failures)}). "
            + $"O jogo pode estar parcialmente alterado: rode vnf restore para devolver os originais de '{backupDirectory}'.",
            cause);
    }

    private (List<string> Restored, List<string> Removed, List<string> Kept, List<string> Failures) RestoreEntries(
        string gameDirectory,
        Func<BackupEntry, string> backupDirectoryOf,
        IEnumerable<BackupEntry> entries,
        bool removeModifiedCreations = false)
    {
        var restored = new List<string>();
        var removed = new List<string>();
        var kept = new List<string>();
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                var target = ResolveInside(gameDirectory, entry.Path);
                if (entry.Existed)
                {
                    RestoreOne(backupDirectoryOf(entry), entry, target);
                    restored.Add(entry.Path);
                }
                else if (File.Exists(target))
                {
                    if (removeModifiedCreations || FileHash.Sha256(target) == entry.InjectedSha256)
                    {
                        _fileSystem.DeleteFile(target);
                        removed.Add(entry.Path);
                    }
                    else
                    {
                        kept.Add(entry.Path);
                    }
                }
            }
            catch (Exception ex) when (ex is InjectionException or IOException or UnauthorizedAccessException)
            {
                failures.Add($"'{entry.Path}': {ex.Message}");
            }
        }

        return (restored, removed, kept, failures);
    }

    private void RestoreOne(string backupDirectory, BackupEntry entry, string target)
    {
        var problem = CheckBackupCopy(backupDirectory, entry);
        if (problem is not null)
        {
            throw new InjectionException(problem);
        }

        _fileSystem.CopyFile(ResolveInside(backupDirectory, entry.Path), target);
        VerifyHash(target, entry.OriginalSha256!, entry.Path);
    }

    private static string? CheckBackupCopy(string backupDirectory, BackupEntry entry)
    {
        try
        {
            var copy = ResolveInside(backupDirectory, entry.Path);
            if (!File.Exists(copy))
            {
                return $"A cópia de '{entry.Path}' não está no backup.";
            }

            return FileHash.Sha256(copy) == entry.OriginalSha256
                ? null
                : $"A cópia de '{entry.Path}' no backup foi alterada e não confere com o hash original.";
        }
        catch (Exception ex) when (ex is InjectionException or IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível ler a cópia de '{entry.Path}' no backup: {ex.Message}";
        }
    }

    private static void VerifyHash(string path, string expected, string relative)
    {
        if (FileHash.Sha256(path) != expected)
        {
            throw new InjectionException($"O arquivo gravado '{relative}' não confere com o hash esperado.");
        }
    }

    private string CreateBackupDirectory(string backupRoot)
    {
        var stamp = _timeProvider.GetLocalNow().ToString(StampFormat, CultureInfo.InvariantCulture);
        var attempt = 1;
        string path;
        do
        {
            var name = attempt == 1 ? stamp : $"{stamp}-{attempt:D2}";
            path = Path.Combine(backupRoot, name);
            attempt++;
        }
        while (Directory.Exists(path));

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InjectionException($"Não foi possível criar a pasta de backup '{path}': {ex.Message}", ex);
        }

        return path;
    }

    private static void WriteManifest(string backupDirectory, BackupManifest manifest)
    {
        var path = Path.Combine(backupDirectory, BackupManifest.FileName);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, manifest.Serialize(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>
    /// Todos os backups do projeto, do mais novo para o mais antigo, restaurados ou não, para a lista da tela Build. Só lê: um manifesto ilegível
    /// vem com <see cref="BackupInfo.Problem"/> em vez de lançar, e uma pasta sem manifesto não é um backup.
    /// </summary>
    public static IReadOnlyList<BackupInfo> ListBackups(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!Directory.Exists(project.BackupDirectory))
        {
            return [];
        }

        var backups = new List<BackupInfo>();
        foreach (var directory in Directory.EnumerateDirectories(project.BackupDirectory).Order(StringComparer.Ordinal).Reverse())
        {
            var manifestPath = Path.Combine(directory, BackupManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var (manifest, problem) = TryReadManifest(manifestPath);
            backups.Add(new BackupInfo(Path.GetFileName(directory), directory, manifest, BackupSize(directory), problem?.Message));
        }

        return backups;
    }

    private static long BackupSize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(f => !string.Equals(Path.GetRelativePath(directory, f), BackupManifest.FileName, PathComparison)
                    && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Backups ainda não restaurados, do mais novo para o mais antigo; um manifesto ilegível lança <see cref="InjectionException"/> ao chegar nele.</summary>
    private static IEnumerable<BackupRecord> ReadBackups(ProjectFolder project)
    {
        foreach (var found in ReadBackupsTolerant(project))
        {
            if (found.Problem is not null)
            {
                throw found.Problem;
            }

            yield return new BackupRecord(found.Directory, found.Manifest!);
        }
    }

    /// <summary>Como <see cref="ReadBackups"/>, mas um manifesto ilegível vem com o <see cref="FoundBackup.Problem"/> em vez de lançar. Backups já restaurados não aparecem.</summary>
    private static IEnumerable<FoundBackup> ReadBackupsTolerant(ProjectFolder project)
    {
        if (!Directory.Exists(project.BackupDirectory))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(project.BackupDirectory).Order(StringComparer.Ordinal).Reverse())
        {
            var manifestPath = Path.Combine(directory, BackupManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var (manifest, problem) = TryReadManifest(manifestPath);
            if (manifest?.RestoredAt is not null)
            {
                continue;
            }

            yield return new FoundBackup(directory, manifest, problem);
        }
    }

    /// <summary>Pastas de jogo que as injeções do projeto registraram nos manifestos, restauradas ou não; manifesto ilegível é ignorado.</summary>
    internal static IEnumerable<string> RecordedGameDirectories(ProjectFolder project)
    {
        if (!Directory.Exists(project.BackupDirectory))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(project.BackupDirectory))
        {
            var manifestPath = Path.Combine(directory, BackupManifest.FileName);
            if (File.Exists(manifestPath) && TryReadManifest(manifestPath).Manifest is { } manifest)
            {
                yield return manifest.GameDirectory;
            }
        }
    }

    private static (BackupManifest? Manifest, InjectionException? Problem) TryReadManifest(string manifestPath)
    {
        try
        {
            var json = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(manifestPath));
            return (BackupManifest.Parse(json.TrimStart((char)0xFEFF), manifestPath), null);
        }
        catch (InjectionException ex)
        {
            return (null, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return (null, new InjectionException($"O manifesto do backup não pôde ser lido: {manifestPath}", ex));
        }
    }

    private sealed record FoundBackup(string Directory, BackupManifest? Manifest, InjectionException? Problem);

    private sealed record BackupRecord(string Directory, BackupManifest Manifest);

    private sealed record HistoryEntry(string Directory, BackupEntry Entry);

    /// <summary>Backups do mais novo para o mais antigo, lidos sob demanda: só quem precisa de um backup velho paga para ler o manifesto dele.</summary>
    private sealed class BackupHistory(IEnumerable<BackupRecord> backups)
    {
        private readonly List<BackupRecord> _read = [];
        private readonly IEnumerator<BackupRecord> _pending = backups.GetEnumerator();

        /// <summary>A entrada mais recente que mexeu em <paramref name="relative"/>, em qualquer backup.</summary>
        public HistoryEntry? FindLatestEntry(string relative)
        {
            var i = 0;
            while (true)
            {
                if (i == _read.Count)
                {
                    if (!_pending.MoveNext())
                    {
                        return null;
                    }

                    _read.Add(_pending.Current);
                }

                var entry = _read[i].Manifest.Files.FirstOrDefault(e => string.Equals(e.Path, relative, PathComparison));
                if (entry is not null)
                {
                    return new HistoryEntry(_read[i].Directory, entry);
                }

                i++;
            }
        }
    }

    private static string ResolveGameDirectory(ProjectFolder project)
    {
        var config = project.LoadConfig();
        var gameDirectory = Path.GetFullPath(config.Game.Path, project.Root);
        if (!Directory.Exists(gameDirectory))
        {
            throw new InjectionException($"A pasta do jogo não existe: {gameDirectory}");
        }

        return gameDirectory;
    }

    private static string ResolveInside(string directory, string relative)
    {
        try
        {
            return GamePaths.Resolve(directory, relative);
        }
        catch (ArgumentException ex)
        {
            throw new InjectionException($"Caminho recusado: {ex.Message}", ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A pasta sem manifesto é ignorada pelo restore; a falha original é a que importa.
        }
    }
}
