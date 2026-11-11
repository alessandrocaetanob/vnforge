namespace VNForge.Core.Library;

/// <summary>
/// O processo de um jogo que abre um lançador antes (<see cref="LibraryGame.OpensLauncher"/>). Enquanto o jogo não aparece, o tempo não conta: o lançador aberto é
/// <see cref="GameProcessState.Waiting"/>. Quando o jogo aparece, vale só o processo dele, e o fim do lançador (ou ele continuar aberto depois do jogo) não muda nada.
/// Se o lançador sai sem o jogo ter aparecido, espera <paramref name="grace"/> por ele antes de dar o fim.
/// Com o lançador aberto e o jogo sem aparecer, espera no máximo <paramref name="maxWait"/>: depois disso a partida acaba sem ter contado.
/// </summary>
/// <param name="launcher">O processo aberto e os filhos dele.</param>
/// <param name="pollGame">Olha o processo do jogo pelo executável dele; <see langword="null"/> quando não há.</param>
public sealed class LauncherAwareProcess(IGameProcess launcher, Func<GameProcessState?> pollGame, TimeProvider? time = null, TimeSpan? grace = null, TimeSpan? maxWait = null) : IGameProcess
{
    public static TimeSpan DefaultGrace { get; } = TimeSpan.FromSeconds(30);

    public static TimeSpan DefaultMaxWait { get; } = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly long _openedAt = (time ?? TimeProvider.System).GetTimestamp();
    private bool _seen;
    private long? _launcherExitedAt;

    public GameProcessState Poll()
    {
        if (pollGame() is { } game)
        {
            _seen = true;
            return game;
        }

        if (_seen)
        {
            return GameProcessState.Exited;
        }

        if (launcher.Poll() != GameProcessState.Exited)
        {
            return _time.GetElapsedTime(_openedAt) < (maxWait ?? DefaultMaxWait) ? GameProcessState.Waiting : GameProcessState.Exited;
        }

        _launcherExitedAt ??= _time.GetTimestamp();
        return _time.GetElapsedTime(_launcherExitedAt.Value) < (grace ?? DefaultGrace) ? GameProcessState.Waiting : GameProcessState.Exited;
    }

    public void Dispose() => launcher.Dispose();
}
