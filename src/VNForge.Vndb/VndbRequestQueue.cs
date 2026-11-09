namespace VNForge.Vndb;

/// <summary>
/// A fila que mantém o cliente dentro do limite da API do VNDB: até 200 pedidos a cada 5 minutos. O relógio é injetável (<see cref="TimeProvider"/>), então o teste do limite
/// roda sem esperar. O outro limite da API, 1 s de execução no servidor por minuto, não dá para medir deste lado: o servidor responde 429 e o cliente chama
/// <see cref="BlockFor"/> para parar a fila pelo tempo que ele pedir.
/// </summary>
public sealed class VndbRequestQueue
{
    public const int DefaultMaxRequests = 200;

    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly Lock _gate = new();
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    public VndbRequestQueue(TimeProvider time, int maxRequests = DefaultMaxRequests, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRequests, 1);
        _time = time;
        _maxRequests = maxRequests;
        _window = window ?? DefaultWindow;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_window, TimeSpan.Zero);
    }

    /// <summary>
    /// Tenta reservar um lugar na janela. Devolve <see cref="TimeSpan.Zero"/> quando o pedido pode sair agora (e o lugar fica gasto) ou quanto falta para
    /// poder; nesse caso nada é reservado.
    /// </summary>
    public TimeSpan Reserve()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            while (_sent.Count > 0 && now - _sent.Peek() >= _window)
            {
                _sent.Dequeue();
            }

            var wait = _blockedUntil > now ? _blockedUntil - now : TimeSpan.Zero;
            if (_sent.Count >= _maxRequests)
            {
                var untilFree = _sent.Peek() + _window - now;
                if (untilFree > wait)
                {
                    wait = untilFree;
                }
            }

            if (wait > TimeSpan.Zero)
            {
                return wait;
            }

            _sent.Enqueue(now);
            return TimeSpan.Zero;
        }
    }

    /// <summary>Espera a vez do pedido. Só volta quando há lugar na janela; cancelar durante a espera não gasta lugar.</summary>
    public async Task WaitTurnAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var wait = Reserve();
            if (wait == TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(wait, _time, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Para a fila por <paramref name="duration"/>, a partir de agora: o servidor devolveu 429.</summary>
    public void BlockFor(TimeSpan duration)
    {
        lock (_gate)
        {
            var until = _time.GetUtcNow() + duration;
            if (until > _blockedUntil)
            {
                _blockedUntil = until;
            }
        }
    }
}
