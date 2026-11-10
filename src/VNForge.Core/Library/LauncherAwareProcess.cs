namespace VNForge.Core.Library;

/// <summary>
/// O processo de um jogo que abre um lançador antes (<see cref="LibraryGame.OpensLauncher"/>). Enquanto o jogo não aparece, o tempo não conta: o lançador aberto é
/// <see cref="GameProcessState.Waiting"/>. Quando o jogo aparece, vale só o processo dele, e o fim do lançador (ou ele continuar aberto depois do jogo) não muda nada.
/// Se o lançador sai sem o jogo ter aparecido, espera <paramref name="grace"/> por ele antes de dar o fim.
/// </summary>
/// <param name="launcher">O processo aberto e os filhos dele.</param>
/// <param name="pollGame">Olha o processo do jogo pelo executável dele; <see langword="null"/> quando não há.</param>
public sealed class LauncherAwareProcess(IGameProcess launcher, Func<GameProcessState?> pollGame, TimeProvider? time = null, TimeSpan? grace = null) : IGameProcess
{
    public static TimeSpan DefaultGrace { get; } = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
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
            return GameProcessState.Waiting;
        }

        _launcherExitedAt ??= _time.GetTimestamp();
        return _time.GetElapsedTime(_launcherExitedAt.Value) < (grace ?? DefaultGrace) ? GameProcessState.Waiting : GameProcessState.Exited;
    }

    public void Dispose() => launcher.Dispose();
}
