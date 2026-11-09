using System.Collections.Concurrent;

namespace VNForge.Core.Images;

/// <summary>Leitor determinístico para testes: sem rede e sem SDK. O texto de cada imagem vem da função recebida; por padrão ninguém tem texto.</summary>
public sealed class FakeImageTextReader(Func<ImageToRead, string>? text = null) : IImageTextReader
{
    private readonly Func<ImageToRead, string> _text = text ?? (_ => string.Empty);
    private readonly ConcurrentQueue<ImageToRead> _received = new();
    private int _count;
    private int _inFlight;
    private int _maxInFlight;

    /// <summary>As imagens recebidas, na ordem das chamadas.</summary>
    public IReadOnlyCollection<ImageToRead> Received => _received;

    /// <summary>Recusa as imagens para as quais devolve verdadeiro.</summary>
    public Func<ImageToRead, bool>? RefuseWhen { get; set; }

    /// <summary>Lança <see cref="InvalidOperationException"/> para as imagens em que devolve verdadeiro.</summary>
    public Func<ImageToRead, bool>? FailWhen { get; set; }

    /// <summary>Chamado no começo de cada leitura com o número dela (1, 2, ...).</summary>
    public Action<int>? OnRead { get; set; }

    /// <summary>Espera antes de responder; serve para os testes de paralelismo.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Tokens que cada leitura informa.</summary>
    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    /// <summary>O maior número de leituras ao mesmo tempo que a instância viu.</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public async Task<ImageTextReading> ReadAsync(ImageToRead image, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        ct.ThrowIfCancellationRequested();
        _received.Enqueue(image);
        OnRead?.Invoke(Interlocked.Increment(ref _count));

        var running = Interlocked.Increment(ref _inFlight);
        try
        {
            int seen;
            while (running > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, running, seen) != seen)
            {
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct).ConfigureAwait(false);
            }

            if (FailWhen?.Invoke(image) == true)
            {
                throw new InvalidOperationException("falha simulada na leitura de " + image.Name);
            }

            return RefuseWhen?.Invoke(image) == true
                ? new ImageTextReading(string.Empty, Refused: true)
                : new ImageTextReading(_text(image), InputTokens: InputTokens, OutputTokens: OutputTokens);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
