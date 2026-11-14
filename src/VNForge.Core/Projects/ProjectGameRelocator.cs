using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Library;

namespace VNForge.Core.Projects;

/// <summary>O que <see cref="ProjectGameRelocator.Move"/> fez.</summary>
/// <param name="GameDirectory">A pasta do jogo agora registrada no projeto.</param>
/// <param name="Changed">Falso quando o projeto já apontava para essa pasta em todo lugar e nada foi gravado.</param>
/// <param name="CheckedFiles">Quantos arquivos da pasta nova foram conferidos contra o projeto.</param>
/// <param name="ManifestsUpdated">Quantos manifestos de backup passaram a apontar para a pasta nova.</param>
public sealed record GameRelocation(string GameDirectory, bool Changed, int CheckedFiles, int ManifestsUpdated);

/// <summary>Em que estado o jogo está na pasta que o projeto vai passar a usar.</summary>
public enum GameFolderState
{
    /// <summary>Os arquivos são os que a extração leu.</summary>
    Original,

    /// <summary>Algum arquivo é o que uma injeção do VNForge gravou e ainda não foi restaurada.</summary>
    Applied,
}

/// <summary>Um arquivo da pasta nova que não confere com o projeto. <paramref name="Reason"/> é <c>diferente</c>, <c>ausente</c> ou <c>caminho fora da pasta</c>.</summary>
public sealed record GameMismatch(string Path, string Reason)
{
    internal const string MissingReason = "ausente";

    public bool IsMissing => Reason == MissingReason;

    public override string ToString() => $"{Path} ({Reason})";
}

/// <summary>
/// O que a troca de pasta acharia, sem gravar nada (<see cref="ProjectGameRelocator.Inspect"/>).
/// <paramref name="Mismatches"/> vazio é uma pasta que serve; <paramref name="AppliedAt"/> é o momento da injeção mais nova ainda não restaurada, quando <paramref name="State"/> é <see cref="GameFolderState.Applied"/>.
/// </summary>
public sealed record GameRelocationPreview(string CurrentFolder, string TargetFolder, GameFolderState State, DateTimeOffset? AppliedAt, IReadOnlyList<GameMismatch> Mismatches)
{
    public bool Accepted => Mismatches.Count == 0;
}

/// <summary>
/// Troca a pasta do jogo de um projeto quando o jogo muda de lugar (#689). A pasta nova só vale se tiver o mesmo jogo: cada arquivo que a extração leu (<c>state.originals</c>)
/// bate com o hash do original ou com o da injeção mais nova ainda não restaurada, e cada arquivo dessas injeções que existe na pasta é o original ou o injetado.
/// Conferido, regrava os manifestos de backup, o <c>.cache/state.json</c> e, por último, o <c>game.path</c> do <c>vnforge.json</c>; uma falha no meio deixa a troca incompleta,
/// mas repetir o comando a conclui. Só lê a pasta do jogo.
/// </summary>
public static class ProjectGameRelocator
{
    /// <summary>Confere a pasta como o <see cref="Move"/> faz e diz em que estado o jogo está, sem gravar nada. Arquivo que não confere vem em <see cref="GameRelocationPreview.Mismatches"/>.</summary>
    /// <exception cref="VNForgeException">A pasta não existe ou o projeto não tem registro do jogo para comparar.</exception>
    public static GameRelocationPreview Inspect(ProjectFolder project, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var target = RequireFolder(gameDirectory);
        var check = Check(target, project.LoadState().Originals, GameInjector.LatestUnrestoredEntries(project));
        var state = check.InjectedFound ? GameFolderState.Applied : GameFolderState.Original;
        var appliedAt = state == GameFolderState.Applied ? GameInjector.LatestUnrestoredInjectionAt(project) : null;
        var current = GameLibrary.NormalizeFolder(Path.GetFullPath(project.LoadConfig().Game.Path, project.Root));
        return new GameRelocationPreview(current, target, state, appliedAt, check.Mismatches);
    }

    /// <exception cref="VNForgeException">A pasta não existe, o projeto não tem registro do jogo para comparar, ou um arquivo não confere (nada é gravado).</exception>
    public static GameRelocation Move(ProjectFolder project, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var target = RequireFolder(gameDirectory);

        using var projectLock = ProjectLock.Acquire(project);
        var config = project.LoadConfig();
        var state = project.LoadState();
        var configStale = !Same(Path.GetFullPath(config.Game.Path, project.Root), target);
        var stateStale = state.GameDirectory is null ? state.Originals.Count > 0 : !Same(state.GameDirectory, target);
        if (!configStale && !stateStale && !GameInjector.AnyManifestElsewhere(project, target))
        {
            return new GameRelocation(target, false, 0, 0);
        }

        var checkedFiles = Verify(target, state.Originals, GameInjector.LatestUnrestoredEntries(project));

        var manifests = GameInjector.RetargetManifests(project, target);
        if (stateStale)
        {
            project.SaveState(state with { GameDirectory = target });
        }

        if (configStale)
        {
            project.SaveConfig(config with { Game = config.Game with { Path = ConfiguredPath(project, config.Game.Path, target) } });
        }

        return new GameRelocation(target, true, checkedFiles, manifests);
    }

    private static string RequireFolder(string gameDirectory)
    {
        var target = GameLibrary.NormalizeFolder(gameDirectory);
        return Directory.Exists(target) ? target : throw new VNForgeException($"A pasta do jogo não existe: {target}");
    }

    // Quem escreveu o game.path relativo ao projeto continua com ele relativo; o absoluto continua absoluto, com "/" como o vnf init grava.
    private static string ConfiguredPath(ProjectFolder project, string current, string target) =>
        (Path.IsPathRooted(current) ? target : Path.GetRelativePath(project.Root, target)).Replace(Path.DirectorySeparatorChar, '/');

    private static bool Same(string a, string b) => string.Equals(GameLibrary.NormalizeFolder(a), GameLibrary.NormalizeFolder(b), GameInjector.PathComparison);

    private static int Verify(string target, IReadOnlyList<OriginalFile> originals, IReadOnlyDictionary<string, BackupEntry> latest)
    {
        var check = Check(target, originals, latest);
        if (check.Mismatches.Count > 0)
        {
            throw new VNForgeException(
                $"A pasta '{target}' não é o mesmo jogo do projeto, então a troca foi recusada e nada foi gravado. Arquivos que não conferem: {string.Join(", ", check.Mismatches)}. "
                + "A pasta nova tem de ter os arquivos como o projeto os viu, originais ou com a injeção do VNForge, sem mudança de fora (uma atualização do jogo, por exemplo).");
        }

        return check.CheckedFiles;
    }

    private sealed record FolderCheck(int CheckedFiles, bool InjectedFound, IReadOnlyList<GameMismatch> Mismatches);

    private static FolderCheck Check(string target, IReadOnlyList<OriginalFile> originals, IReadOnlyDictionary<string, BackupEntry> latest)
    {
        if (originals.Count == 0 && latest.Count == 0)
        {
            throw new VNForgeException("O projeto não guardou nenhum arquivo do jogo (nem na extração, nem numa injeção), então não há como conferir que a pasta nova é o mesmo jogo. Rode vnf extract com a pasta nova.");
        }

        var problems = new List<GameMismatch>();
        var injected = false;
        foreach (var original in originals.OrderBy(o => o.Path, StringComparer.Ordinal))
        {
            var injection = latest.GetValueOrDefault(original.Path);
            injected |= Matches(problems, target, original.Path, original.Sha256, injection?.InjectedSha256, mustExist: true);
        }

        var known = originals.Select(o => o.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = latest.Values.Where(e => !known.Contains(e.Path)).OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
        foreach (var entry in others)
        {
            injected |= Matches(problems, target, entry.Path, entry.OriginalSha256, entry.InjectedSha256, mustExist: false);
        }

        return new FolderCheck(originals.Count + others.Count, injected, problems);
    }

    /// <summary>Confere um arquivo contra o hash do original e o da injeção; devolve se ele é o que a injeção gravou (e não o original).</summary>
    private static bool Matches(List<GameMismatch> problems, string target, string relative, string? originalHash, string? injectedHash, bool mustExist)
    {
        string path;
        try
        {
            path = GamePaths.Resolve(target, relative);
        }
        catch (ArgumentException)
        {
            problems.Add(new GameMismatch(relative, "caminho fora da pasta"));
            return false;
        }

        if (!File.Exists(path))
        {
            if (mustExist)
            {
                problems.Add(new GameMismatch(relative, GameMismatch.MissingReason));
            }

            return false;
        }

        try
        {
            var hash = FileHash.Sha256(path);
            if (hash != originalHash && hash != injectedHash)
            {
                problems.Add(new GameMismatch(relative, "diferente"));
                return false;
            }

            return hash == injectedHash && hash != originalHash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível ler '{relative}' para conferir o jogo (o jogo está aberto?): {ex.Message}", ex);
        }
    }
}
