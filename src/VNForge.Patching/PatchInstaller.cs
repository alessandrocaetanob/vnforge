using System.Text.Json;

namespace VNForge.Patching;

public enum PatchCheckState
{
    /// <summary>Todos os arquivos estão como o patch espera (ou já com o patch aplicado, de uma instalação interrompida) e há o que aplicar.</summary>
    Ready,

    /// <summary>Todos os arquivos já são os do patch.</summary>
    AlreadyInstalled,

    /// <summary>Algum arquivo não é o da versão de referência: nada será gravado.</summary>
    WrongVersion,
}

public enum PatchFileState
{
    /// <summary>O arquivo é o original que o patch espera.</summary>
    Original,

    /// <summary>O arquivo é o de um patch anterior nosso; o original está no backup.</summary>
    Upgrade,

    /// <summary>O arquivo já é o resultado do patch.</summary>
    Patched,

    /// <summary>O arquivo que o patch espera encontrar não existe.</summary>
    Missing,

    /// <summary>O arquivo existe e não é o original, nem o patch.</summary>
    Different,
}

/// <param name="Expected">SHA-256 do original que o patch espera; nulo para arquivo criado.</param>
/// <param name="Found">SHA-256 do arquivo na pasta; nulo quando ele não existe.</param>
public sealed record PatchFileStatus(string Path, PatchFileState State, string? Expected, string? Found)
{
    internal string? SourcePath { get; init; }
}

/// <param name="InstalledVersion">A versão do patch que a pasta já tem instalada; nula quando não há instalação nossa.</param>
public sealed record PatchCheck(PatchCheckState State, IReadOnlyList<PatchFileStatus> Files, string? InstalledVersion);

public sealed record PatchInstallResult(IReadOnlyList<string> Written, string BackupDirectory);

/// <param name="Kept">Arquivos que mudaram depois da instalação: a desinstalação os deixa em paz.</param>
public sealed record PatchUninstallResult(IReadOnlyList<string> Restored, IReadOnlyList<string> Removed, IReadOnlyList<string> Kept);

/// <summary>
/// Aplica e desfaz o patch na pasta do jogo. Só toca um jogo cujos arquivos têm o SHA-256 da versão de referência; guarda os originais em
/// <see cref="BackupFolderName"/> e confere a cópia antes de gravar qualquer coisa (regra 2 do CLAUDE.md); grava cada arquivo num temporário e só então o
/// troca, e se algo falha no meio devolve o que já trocou.
/// </summary>
public static class PatchInstaller
{
    /// <summary>Pasta, dentro da do jogo, com os originais trocados e o manifesto da instalação.</summary>
    public const string BackupFolderName = "_originais";

    private const string ManifestFileName = "manifest.json";

    private const string TempSuffix = ".vnf-tmp";

    private const string PreviousFolderName = ".anterior";

    public static PatchCheck Check(string gameDirectory, PatchPackage package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(package);
        if (!Directory.Exists(gameDirectory))
        {
            throw new PatchException($"A pasta '{gameDirectory}' não existe.");
        }

        var installed = ReadManifest(gameDirectory) is { Installed: true } manifest ? manifest : null;
        var backupRoot = Path.Combine(gameDirectory, BackupFolderName);
        var files = new List<PatchFileStatus>();
        foreach (var entry in package.Manifest.Files)
        {
            var target = GamePaths.Resolve(gameDirectory, entry.Path);
            var found = GamePaths.ShaOrNull(target);
            var prior = installed?.Files.FirstOrDefault(f => string.Equals(f.Path, entry.Path, StringComparison.Ordinal));
            files.Add(Classify(entry, target, found, prior, backupRoot));
        }

        return new PatchCheck(StateOf(files), files, installed?.Version);
    }

    private static PatchCheckState StateOf(List<PatchFileStatus> files)
    {
        if (files.Any(f => f.State is PatchFileState.Missing or PatchFileState.Different))
        {
            return PatchCheckState.WrongVersion;
        }

        return files.All(f => f.State == PatchFileState.Patched) ? PatchCheckState.AlreadyInstalled : PatchCheckState.Ready;
    }

    /// <exception cref="PatchException">A pasta não é da versão de referência (nada foi gravado), o patch está corrompido, ou a gravação falhou (o que já tinha sido trocado foi devolvido).</exception>
    public static PatchInstallResult Install(string gameDirectory, PatchPackage package, IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new PatchProgress(PatchPhase.Check, null, 0, package.Manifest.Files.Count));
        var check = Check(gameDirectory, package);
        var backupRoot = Path.Combine(gameDirectory, BackupFolderName);
        if (check.State == PatchCheckState.WrongVersion)
        {
            throw new PatchException(DescribeMismatch(check));
        }

        if (check.State == PatchCheckState.AlreadyInstalled)
        {
            return new PatchInstallResult([], backupRoot);
        }

        var entries = package.Manifest.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var previousManifest = ReadManifest(gameDirectory);
        var records = (previousManifest?.Files ?? []).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var todo = check.Files.Where(f => f.State != PatchFileState.Patched).ToList();
        var undo = new List<UndoStep>();
        var previousRoot = Path.Combine(backupRoot, PreviousFolderName);

        Directory.CreateDirectory(backupRoot);
        for (var i = 0; i < todo.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = todo[i];
            var entry = entries[status.Path];
            progress?.Report(new PatchProgress(PatchPhase.Backup, status.Path, i, todo.Count));
            var target = GamePaths.Resolve(gameDirectory, status.Path);
            if (!entry.Created && status.State == PatchFileState.Original)
            {
                BackUp(target, GamePaths.Resolve(backupRoot, status.Path), entry.OriginalSha256!);
                undo.Add(new UndoStep(status.Path, GamePaths.Resolve(backupRoot, status.Path), entry.OriginalSha256));
            }
            else if (status.State == PatchFileState.Upgrade)
            {
                undo.Add(Preserve(target, previousRoot, status.Path, status.Found!));
            }
            else
            {
                undo.Add(new UndoStep(status.Path, null, null));
            }

            records[status.Path] = new InstallRecord(
                status.Path,
                Existed: !entry.Created,
                entry.OriginalSha256,
                entry.ResultSha256);
        }

        foreach (var path in check.Files.Where(f => f.State == PatchFileState.Patched && records.ContainsKey(f.Path)).Select(f => f.Path))
        {
            records[path] = records[path] with { InstalledSha256 = entries[path].ResultSha256 };
        }

        // O manifesto vai antes do primeiro arquivo trocado: se a máquina cair no meio, a desinstalação já sabe o que desfazer.
        var installManifest = new InstallManifest(package.Manifest.GameName, package.Manifest.Version, Installed: true, records.Values.OrderBy(r => r.Path, StringComparer.Ordinal).ToList());
        WriteManifest(gameDirectory, installManifest);

        var written = new List<string>();
        try
        {
            for (var i = 0; i < todo.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = todo[i];
                progress?.Report(new PatchProgress(PatchPhase.Apply, status.Path, i, todo.Count));
                ApplyOne(gameDirectory, package, entries[status.Path], status);
                written.Add(status.Path);
            }

            installManifest = RestoreSuperseded(gameDirectory, backupRoot, previousRoot, installManifest, entries, undo);
            WriteManifest(gameDirectory, installManifest);

            for (var i = 0; i < written.Count; i++)
            {
                progress?.Report(new PatchProgress(PatchPhase.Verify, written[i], i, written.Count));
                var target = GamePaths.Resolve(gameDirectory, written[i]);
                if (GamePaths.ShaOrNull(target) != entries[written[i]].ResultSha256)
                {
                    throw new PatchException($"O arquivo {written[i]} não ficou como o patch previa.");
                }
            }
        }
        catch (Exception ex)
        {
            var failures = RollBack(gameDirectory, undo, previousManifest ?? new InstallManifest(package.Manifest.GameName, package.Manifest.Version, Installed: false, []));
            if (failures.Count > 0)
            {
                throw new PatchException(
                    $"Não foi possível aplicar o patch: {ex.Message} Não consegui devolver: {string.Join("; ", failures)}. As cópias do estado anterior ficaram em '{BackupFolderName}/{PreviousFolderName}'; rode o instalador e escolha desinstalar.",
                    ex);
            }

            DeleteFolderQuietly(previousRoot);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new PatchException($"{(ex is PatchException ? ex.Message : "Não foi possível aplicar o patch: " + ex.Message)} Os arquivos do jogo voltaram ao estado de antes desta instalação.", ex);
        }

        DeleteFolderQuietly(previousRoot);
        progress?.Report(new PatchProgress(PatchPhase.Verify, null, written.Count, written.Count));
        return new PatchInstallResult(written, backupRoot);
    }

    /// <summary>Devolve os originais guardados. Arquivo que mudou depois da instalação fica como está e vai em <see cref="PatchUninstallResult.Kept"/>.</summary>
    /// <exception cref="PatchException">Não há instalação nesta pasta, ou a cópia de um original sumiu ou não confere (nada foi trocado).</exception>
    public static PatchUninstallResult Uninstall(string gameDirectory, IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var manifest = ReadManifest(gameDirectory);
        if (manifest is not { Installed: true })
        {
            throw new PatchException("Não achei uma tradução instalada por este instalador nesta pasta.");
        }

        var backupRoot = Path.Combine(gameDirectory, BackupFolderName);
        var restore = new List<InstallRecord>();
        var remove = new List<InstallRecord>();
        var kept = new List<string>();
        foreach (var record in manifest.Files)
        {
            var target = GamePaths.Resolve(gameDirectory, record.Path);
            var current = GamePaths.ShaOrNull(target);
            if (!record.Existed)
            {
                if (current is null)
                {
                    continue;
                }

                if (current == record.InstalledSha256)
                {
                    remove.Add(record);
                }
                else
                {
                    kept.Add(record.Path);
                }

                continue;
            }

            if (current == record.OriginalSha256)
            {
                continue;
            }

            if (current is not null && current != record.InstalledSha256)
            {
                kept.Add(record.Path);
                continue;
            }

            var backup = GamePaths.Resolve(backupRoot, record.Path);
            if (GamePaths.ShaOrNull(backup) != record.OriginalSha256)
            {
                throw new PatchException($"A cópia do original de {record.Path} em '{BackupFolderName}' sumiu ou não confere. Nada foi alterado; reinstale o jogo.");
            }

            restore.Add(record);
        }

        var total = restore.Count + remove.Count;
        var restored = new List<string>();
        foreach (var record in restore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PatchProgress(PatchPhase.Restore, record.Path, restored.Count, total));
            CopyReplacing(GamePaths.Resolve(backupRoot, record.Path), GamePaths.Resolve(gameDirectory, record.Path), record.OriginalSha256!);
            restored.Add(record.Path);
        }

        var removed = new List<string>();
        foreach (var path in remove.Select(record => record.Path))
        {
            progress?.Report(new PatchProgress(PatchPhase.Restore, path, restored.Count + removed.Count, total));
            File.Delete(GamePaths.Resolve(gameDirectory, path));
            removed.Add(path);
        }

        WriteManifest(gameDirectory, manifest with { Installed = false });
        progress?.Report(new PatchProgress(PatchPhase.Restore, null, total, total));
        return new PatchUninstallResult(restored, removed, kept);
    }

    /// <summary>A versão instalada nesta pasta, se houver uma instalação nossa.</summary>
    public static string? InstalledVersion(string gameDirectory) =>
        ReadManifest(gameDirectory) is { Installed: true } manifest ? manifest.Version : null;

    public static string DescribeMismatch(PatchCheck check)
    {
        var lines = check.Files
            .Where(f => f.State is PatchFileState.Missing or PatchFileState.Different)
            .Select(f => f.Found is null
                ? $"  {f.Path}: não encontrei o arquivo."
                : $"  {f.Path}: esperado SHA-256 {Short(f.Expected)}, encontrado {Short(f.Found)}.");
        return "Esta pasta não tem a versão do jogo para a qual o patch foi feito. Nada foi alterado.\n" + string.Join('\n', lines);
    }

    private static string Short(string? sha) => sha is { Length: > 12 } ? $"{sha[..4]}…{sha[^4..]}" : sha ?? "(nenhum)";

    private static PatchFileStatus Classify(PatchFileEntry entry, string target, string? found, InstallRecord? prior, string backupRoot)
    {
        PatchFileStatus Status(PatchFileState state, string? source = null) =>
            new(entry.Path, state, entry.OriginalSha256, found) { SourcePath = source };

        if (found == entry.ResultSha256)
        {
            return Status(PatchFileState.Patched);
        }

        if (entry.Created)
        {
            if (found is null)
            {
                return Status(PatchFileState.Original);
            }

            return prior is { Existed: false } && found == prior.InstalledSha256 ? Status(PatchFileState.Upgrade) : Status(PatchFileState.Different);
        }

        if (found is null)
        {
            return Status(PatchFileState.Missing);
        }

        if (found == entry.OriginalSha256)
        {
            return Status(PatchFileState.Original, target);
        }

        if (prior is { Existed: true } && found == prior.InstalledSha256 && prior.OriginalSha256 == entry.OriginalSha256)
        {
            var backup = GamePaths.Resolve(backupRoot, entry.Path);
            if (GamePaths.ShaOrNull(backup) == entry.OriginalSha256)
            {
                return Status(PatchFileState.Upgrade, backup);
            }
        }

        return Status(PatchFileState.Different);
    }

    private static void BackUp(string target, string backup, string expectedSha)
    {
        if (GamePaths.ShaOrNull(backup) == expectedSha)
        {
            return;
        }

        CopyReplacing(target, backup, expectedSha);
    }

    /// <summary>Copia para um temporário ao lado do destino, confere o hash e só então troca; o destino nunca fica pela metade.</summary>
    private static void CopyReplacing(string from, string to, string expectedSha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        var temp = to + TempSuffix;
        try
        {
            File.Copy(from, temp, overwrite: true);
            if (GamePaths.Sha256(temp) != expectedSha)
            {
                throw new PatchException($"A cópia de {Path.GetFileName(to)} não confere com o original.");
            }

            File.Move(temp, to, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new PatchException($"Não foi possível gravar {Path.GetFileName(to)}: {ex.Message} Feche o jogo e tente de novo.", ex);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static void ApplyOne(string gameDirectory, PatchPackage package, PatchFileEntry entry, PatchFileStatus status)
    {
        var target = GamePaths.Resolve(gameDirectory, entry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + TempSuffix;
        try
        {
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (entry.Created)
                {
                    package.Apply(entry, null, output);
                }
                else
                {
                    using var source = new FileStream(status.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
                    package.Apply(entry, source, output);
                }
            }

            if (GamePaths.Sha256(temp) != entry.ResultSha256)
            {
                throw new PatchException($"O patch gerou um {entry.Path} diferente do esperado: o arquivo está corrompido.");
            }

            File.Move(temp, target, overwrite: true);
        }
        catch (Exception ex) when (ex is not PatchException and not OperationCanceledException)
        {
            var hint = ex is IOException or UnauthorizedAccessException ? " Feche o jogo e confira se a pasta aceita gravação." : string.Empty;
            throw new PatchException($"Não foi possível aplicar o patch em {entry.Path}: {ex.Message}{hint}", ex);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Arquivos que um patch anterior nosso mexeu e o novo não: voltam ao original, senão ficariam com a tradução velha.</summary>
    private static InstallManifest RestoreSuperseded(string gameDirectory, string backupRoot, string previousRoot, InstallManifest manifest, Dictionary<string, PatchFileEntry> entries, List<UndoStep> undo)
    {
        var keep = new List<InstallRecord>();
        foreach (var record in manifest.Files)
        {
            if (entries.ContainsKey(record.Path))
            {
                keep.Add(record);
                continue;
            }

            var target = GamePaths.Resolve(gameDirectory, record.Path);
            var current = GamePaths.ShaOrNull(target);
            if (current != record.InstalledSha256)
            {
                keep.Add(record);
            }
            else
            {
                undo.Add(Preserve(target, previousRoot, record.Path, current));
                if (record.Existed)
                {
                    CopyReplacing(GamePaths.Resolve(backupRoot, record.Path), target, record.OriginalSha256!);
                }
                else
                {
                    File.Delete(target);
                }
            }
        }

        return manifest with { Files = keep };
    }

    /// <summary>Copia o arquivo como está agora (o de uma instalação anterior) para <see cref="PreviousFolderName"/>, para a falha da atualização poder voltar a ele e não ao original.</summary>
    private static UndoStep Preserve(string target, string previousRoot, string relative, string sha)
    {
        var copy = GamePaths.Resolve(previousRoot, relative);
        CopyReplacing(target, copy, sha);
        return new UndoStep(relative, copy, sha);
    }

    /// <summary>Devolve cada arquivo ao estado de antes da instalação, do último para o primeiro, e o manifesto ao que era. Devolve o que não voltou.</summary>
    private static List<string> RollBack(string gameDirectory, List<UndoStep> undo, InstallManifest manifestBefore)
    {
        var failures = new List<string>();
        for (var i = undo.Count - 1; i >= 0; i--)
        {
            var step = undo[i];
            try
            {
                var target = GamePaths.Resolve(gameDirectory, step.Path);
                var current = GamePaths.ShaOrNull(target);
                if (step.RestoreFrom is null)
                {
                    if (current is not null)
                    {
                        File.Delete(target);
                    }
                }
                else if (current != step.Sha)
                {
                    CopyReplacing(step.RestoreFrom, target, step.Sha!);
                }
            }
            catch (Exception ex) when (ex is PatchException or IOException or UnauthorizedAccessException)
            {
                failures.Add($"{step.Path} ({ex.Message})");
            }
        }

        try
        {
            WriteManifest(gameDirectory, manifestBefore);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{BackupFolderName}/{ManifestFileName} ({ex.Message})");
        }

        return failures;
    }

    private static void DeleteFolderQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // É só a cópia temporária do estado anterior; sobra um lixo, não um erro.
        }
    }

    private sealed record UndoStep(string Path, string? RestoreFrom, string? Sha);

    internal static InstallManifest? ReadManifest(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, BackupFolderName, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllBytes(path), PatchManifest.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteManifest(string gameDirectory, InstallManifest manifest)
    {
        var path = Path.Combine(gameDirectory, BackupFolderName, ManifestFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + TempSuffix;
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(manifest, PatchManifest.Json));
        File.Move(temp, path, overwrite: true);
    }

    internal sealed record InstallRecord(string Path, bool Existed, string? OriginalSha256, string InstalledSha256);

    internal sealed record InstallManifest(string GameName, string Version, bool Installed, IReadOnlyList<InstallRecord> Files);
}
