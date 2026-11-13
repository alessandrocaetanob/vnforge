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

/// <summary>
/// Troca a pasta do jogo de um projeto quando o jogo muda de lugar (#689). A pasta nova só vale se tiver o mesmo jogo: cada arquivo que a extração leu (<c>state.originals</c>)
/// bate com o hash do original ou com o da injeção mais nova ainda não restaurada, e cada arquivo dessas injeções que existe na pasta é o original ou o injetado.
/// Conferido, regrava os manifestos de backup, o <c>.cache/state.json</c> e, por último, o <c>game.path</c> do <c>vnforge.json</c>; uma falha no meio deixa a troca incompleta,
/// mas repetir o comando a conclui. Só lê a pasta do jogo.
/// </summary>
public static class ProjectGameRelocator
{
    /// <exception cref="VNForgeException">A pasta não existe, o projeto não tem registro do jogo para comparar, ou um arquivo não confere (nada é gravado).</exception>
    public static GameRelocation Move(ProjectFolder project, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var target = GameLibrary.NormalizeFolder(gameDirectory);
        if (!Directory.Exists(target))
        {
            throw new VNForgeException($"A pasta do jogo não existe: {target}");
        }

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

    // Quem escreveu o game.path relativo ao projeto continua com ele relativo; o absoluto continua absoluto, com "/" como o vnf init grava.
    private static string ConfiguredPath(ProjectFolder project, string current, string target) =>
        (Path.IsPathRooted(current) ? target : Path.GetRelativePath(project.Root, target)).Replace(Path.DirectorySeparatorChar, '/');

    private static bool Same(string a, string b) => string.Equals(GameLibrary.NormalizeFolder(a), GameLibrary.NormalizeFolder(b), GameInjector.PathComparison);

    private static int Verify(string target, IReadOnlyList<OriginalFile> originals, IReadOnlyDictionary<string, BackupEntry> latest)
    {
        if (originals.Count == 0 && latest.Count == 0)
        {
            throw new VNForgeException("O projeto não guardou nenhum arquivo do jogo (nem na extração, nem numa injeção), então não há como conferir que a pasta nova é o mesmo jogo. Rode vnf extract com a pasta nova.");
        }

        var problems = new List<string>();
        foreach (var original in originals.OrderBy(o => o.Path, StringComparer.Ordinal))
        {
            string?[] accepted = latest.TryGetValue(original.Path, out var injection) ? [original.Sha256, injection.InjectedSha256] : [original.Sha256];
            AddProblem(problems, target, original.Path, accepted, mustExist: true);
        }

        var known = originals.Select(o => o.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = latest.Values.Where(e => !known.Contains(e.Path)).OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
        foreach (var entry in others)
        {
            AddProblem(problems, target, entry.Path, [entry.OriginalSha256, entry.InjectedSha256], mustExist: false);
        }

        if (problems.Count > 0)
        {
            throw new VNForgeException(
                $"A pasta '{target}' não é o mesmo jogo do projeto, então a troca foi recusada e nada foi gravado. Arquivos que não conferem: {string.Join(", ", problems)}. "
                + "A pasta nova tem de ter os arquivos como o projeto os viu, originais ou com a injeção do VNForge, sem mudança de fora (uma atualização do jogo, por exemplo).");
        }

        return originals.Count + others.Count;
    }

    private static void AddProblem(List<string> problems, string target, string relative, string?[] accepted, bool mustExist)
    {
        string path;
        try
        {
            path = GamePaths.Resolve(target, relative);
        }
        catch (ArgumentException)
        {
            problems.Add($"{relative} (caminho fora da pasta)");
            return;
        }

        if (!File.Exists(path))
        {
            if (mustExist)
            {
                problems.Add($"{relative} (ausente)");
            }

            return;
        }

        try
        {
            if (!accepted.Contains(FileHash.Sha256(path)))
            {
                problems.Add($"{relative} (diferente)");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível ler '{relative}' para conferir o jogo (o jogo está aberto?): {ex.Message}", ex);
        }
    }
}
