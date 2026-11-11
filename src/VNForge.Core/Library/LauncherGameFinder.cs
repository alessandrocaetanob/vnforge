namespace VNForge.Core.Library;

/// <summary>Um programa em execução: o id do processo e o caminho do executável, ou <see langword="null"/> quando o sistema não deixou ler (processo de administrador).</summary>
public sealed record RunningProgram(int Id, string? Path);

/// <summary>
/// Acha o processo do jogo quando o executável que a Biblioteca guarda é um lançador (<see cref="LibraryGame.OpensLauncher"/>): o jogo é outro programa da pasta
/// dele, aberto depois. O próprio lançador, o que já rodava antes de abrir e o que não se confirma pelo caminho não valem.
/// </summary>
public static class LauncherGameFinder
{
    /// <param name="running">Os programas em execução agora.</param>
    /// <param name="launcherExecutable">O executável do lançador, que o jogo não pode ser.</param>
    /// <param name="gameFolder">A pasta do jogo: o executável do jogo fica dentro dela.</param>
    /// <param name="alreadyRunning">Os ids que já rodavam antes de o lançador abrir.</param>
    public static IReadOnlyList<int> Find(IEnumerable<RunningProgram> running, string launcherExecutable, string gameFolder, IReadOnlySet<int> alreadyRunning)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(alreadyRunning);
        var launcher = Path.GetFullPath(launcherExecutable);
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder)) + Path.DirectorySeparatorChar;

        return
        [
            .. running
                .Where(program => !alreadyRunning.Contains(program.Id) && Confirmed(program.Path, launcher, folder))
                .Select(program => program.Id),
        ];
    }

    // Sem o caminho não há como confirmar que o processo é o jogo: ficar de fora é o lado seguro, porque contar um programa qualquer inflaria o tempo de jogo.
    private static bool Confirmed(string? path, string launcher, string folder)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, launcher, StringComparison.OrdinalIgnoreCase);
    }
}
