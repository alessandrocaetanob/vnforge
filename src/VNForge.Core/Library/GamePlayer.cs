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
public sealed record PlayTrackingOptions(TimeSpan PollInterval, TimeSpan MaxSuspended)
{
    public static PlayTrackingOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
}

/// <summary>
/// Abre o jogo e conta o tempo de jogo pelo processo: começa quando o processo abre e termina quando ele e os filhos saem, o que cobre o lançador que abre
/// outro executável antes do jogo. O relógio é o <see cref="TimeProvider"/>, para os testes andarem o tempo sem esperar.
/// </summary>
public sealed class GamePlayer(IGameLauncher launcher, PlayLogFile log, TimeProvider? time = null, PlayTrackingOptions? options = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly PlayTrackingOptions _options = options ?? PlayTrackingOptions.Default;

    /// <summary>
    /// Abre o jogo, espera ele fechar e grava a sessão em <c>playtime.json</c>. Se <paramref name="token"/> for cancelado, a contagem para e a sessão
    /// é gravada como interrompida; o jogo continua aberto, o VNForge nunca o encerra.
    /// </summary>
    /// <exception cref="LibraryException">O programa não abriu, ou o <c>playtime.json</c> existe e é ilegível (nada é gravado nesse caso).</exception>
    public async Task<PlaySession> PlayAsync(string gameId, GameLaunch launch, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(launch);

        // Falha antes de abrir o jogo, não depois de uma hora de partida.
        log.Load();

        using var process = launcher.Start(launch);
        var session = await Track(process, token);
        log.Record(gameId, session);
        return session;
    }

    private async Task<PlaySession> Track(IGameProcess process, CancellationToken token)
    {
        var startedAt = _time.GetUtcNow();
        var last = startedAt;
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
            var now = _time.GetUtcNow();
            var step = now - last;
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
                return new PlaySession(startedAt, now, counted, interrupted);
            }
        }
    }

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
