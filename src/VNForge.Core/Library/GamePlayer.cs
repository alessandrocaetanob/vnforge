namespace VNForge.Core.Library;

public enum GameProcessState
{
    /// <summary>Há processo do jogo (ou de um filho dele) rodando.</summary>
    Running,

    /// <summary>Há processos, mas todos estão suspensos (a pessoa suspendeu o jogo, ou o sistema o congelou).</summary>
    Suspended,

    /// <summary>O processo aberto e todos os filhos saíram.</summary>
    Exited,
}

/// <summary>O jogo aberto: o processo e os que ele abre. Quem fecha é o <see cref="IDisposable.Dispose"/>, que não encerra o jogo.</summary>
public interface IGameProcess : IDisposable
{
    /// <summary>Olha o processo agora. Nunca lança por processo que sumiu no meio da consulta.</summary>
    GameProcessState Poll();
}

/// <summary>Abre o jogo. Fica atrás de uma interface para os testes não abrirem nada de verdade.</summary>
public interface IGameLauncher
{
    /// <exception cref="LibraryException">O sistema não conseguiu abrir o programa.</exception>
    IGameProcess Start(GameLaunch launch);
}

/// <param name="PollInterval">De quanto em quanto tempo olhar o processo; também é a precisão do fim da contagem. Zero só serve para teste.</param>
/// <param name="MaxSuspended">Um trecho suspenso maior que isto não entra no tempo de jogo; até isto, entra (uma travada curta não é pausa).</param>
/// <param name="Heartbeat">De quanto em quanto tempo a sessão aberta grava o último instante visto em <c>playtime.json</c>.</param>
/// <param name="MaxStep">O máximo que uma consulta acrescenta ao tempo. O PC que dorme ou hiberna faz o relógio saltar horas entre duas consultas, e esse salto não é jogo.</param>
public sealed record PlayTrackingOptions(TimeSpan PollInterval, TimeSpan MaxSuspended, TimeSpan Heartbeat, TimeSpan MaxStep)
{
    public static PlayTrackingOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10));

    /// <summary>Depois deste tempo sem atualizar, uma sessão aberta é dada como interrompida.</summary>
    public TimeSpan StaleAfter => (Heartbeat * 3) + PollInterval;
}

/// <summary>
/// Abre o jogo e conta o tempo de jogo pelo processo: começa quando o processo abre e termina quando ele e os filhos saem, o que cobre o lançador que abre
/// outro executável antes do jogo. O tempo é medido com o relógio monotônico do <see cref="TimeProvider"/> (<see cref="TimeProvider.GetTimestamp"/>); o relógio de
/// parede só dá o <see cref="PlaySession.StartedAt"/>, então acertar a hora do PC no meio da partida não muda nada. Os testes andam o tempo sem esperar.
/// </summary>
public sealed class GamePlayer(IGameLauncher launcher, PlayLogFile log, TimeProvider? time = null, PlayTrackingOptions? options = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly PlayTrackingOptions _options = options ?? PlayTrackingOptions.Default;

    /// <summary>
    /// Abre o jogo, grava a sessão como aberta, espera o jogo fechar e a fecha em <c>playtime.json</c>. A sessão aberta recebe o último instante visto a cada
    /// <see cref="PlayTrackingOptions.Heartbeat"/>: se o app ou o terminal fechar no meio, ela fica como interrompida, com o que foi contado, e não some.
    /// Se <paramref name="token"/> for cancelado, a contagem para e a sessão é gravada como interrompida; o jogo continua aberto, o VNForge nunca o encerra.
    /// </summary>
    /// <exception cref="LibraryException">O programa não abriu, ou o <c>playtime.json</c> existe e é ilegível (nada é aberto nesse caso).</exception>
    public async Task<PlaySession> PlayAsync(string gameId, GameLaunch launch, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(launch);

        // Falha antes de abrir o jogo, não depois de uma hora de partida.
        log.Load();

        using var process = launcher.Start(launch);
        var startedAt = _time.GetUtcNow();
        var origin = _time.GetTimestamp();
        var open = new PlaySession(PlaySession.NewId(), startedAt, startedAt, TimeSpan.Zero, Open: true);
        log.Update(played =>
        {
            played.CloseStale(startedAt, _options.StaleAfter);
            played.Upsert(gameId, open);
            return 0;
        });

        var closed = await Track(process, gameId, open, origin, token);
        log.Record(gameId, closed);
        return closed;
    }

    private async Task<PlaySession> Track(IGameProcess process, string gameId, PlaySession open, long origin, CancellationToken token)
    {
        var last = origin;
        var lastBeat = origin;
        var counted = TimeSpan.Zero;
        var suspended = TimeSpan.Zero;
        var interrupted = false;

        while (true)
        {
            try
            {
                await Task.Delay(_options.PollInterval, _time, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                interrupted = true;
            }

            var state = interrupted ? StateBeforeStopping(suspended) : process.Poll();
            var now = _time.GetTimestamp();
            var step = Min(_time.GetElapsedTime(last, now), _options.MaxStep);
            last = now;
            if (state == GameProcessState.Suspended)
            {
                suspended += step;
            }
            else
            {
                counted += step + SettleSuspended(ref suspended);
            }

            if (interrupted || state == GameProcessState.Exited)
            {
                // Interrompida no meio de uma suspensão: o trecho aberto vale pelas mesmas regras de um que acabou.
                counted += SettleSuspended(ref suspended);
                return open with { EndedAt = open.StartedAt + _time.GetElapsedTime(origin, now), Duration = counted, Interrupted = interrupted, Open = false };
            }

            if (_time.GetElapsedTime(lastBeat, now) >= _options.Heartbeat)
            {
                lastBeat = now;
                Beat(gameId, open with { EndedAt = open.StartedAt + _time.GetElapsedTime(origin, now), Duration = counted });
            }
        }
    }

    private void Beat(string gameId, PlaySession seen)
    {
        try
        {
            log.Record(gameId, seen);
        }
        catch (LibraryException)
        {
            // O último instante visto é só uma rede de segurança: se o arquivo estiver em uso, a próxima batida tenta de novo e o fim da sessão grava de qualquer jeito.
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    // Cancelado, o processo não é consultado de novo: vale o que a última consulta viu.
    private static GameProcessState StateBeforeStopping(TimeSpan suspended) =>
        suspended > TimeSpan.Zero ? GameProcessState.Suspended : GameProcessState.Running;

    private TimeSpan SettleSuspended(ref TimeSpan suspended)
    {
        var counts = suspended <= _options.MaxSuspended ? suspended : TimeSpan.Zero;
        suspended = TimeSpan.Zero;
        return counts;
    }
}
