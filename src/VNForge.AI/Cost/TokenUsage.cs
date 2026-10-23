namespace VNForge.AI.Cost;

/// <summary>Tokens de uma ou mais chamadas, separados como a API os cobra.</summary>
public readonly record struct TokenUsage(long Input, long Output, long CacheRead = 0, long CacheWrite = 0)
{
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite);

    public long Total => Input + Output + CacheRead + CacheWrite;
}

/// <summary>Acumula a contagem real de tokens de uma execução; seguro para chamadas em paralelo.</summary>
public sealed class UsageLedger
{
    private readonly Lock _gate = new();
    private TokenUsage _total;
    private int _calls;
    private int _cacheHits;

    public void Record(TokenUsage usage)
    {
        lock (_gate)
        {
            _total += usage;
            _calls++;
        }
    }

    /// <summary>Registra linhas servidas pelo cache de traduções, sem chamar a API.</summary>
    public void RecordCacheHits(int lines)
    {
        lock (_gate)
            _cacheHits += lines;
    }

    public TokenUsage Total { get { lock (_gate) return _total; } }
    public int Calls { get { lock (_gate) return _calls; } }
    public int CacheHits { get { lock (_gate) return _cacheHits; } }
}
