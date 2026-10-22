namespace VNForge.Engines.YuRis.Tests.Ypf;

/// <summary>PRNG determinístico (SplitMix64) para a reprodutibilidade do fuzz dos testes; não serve para segurança.</summary>
internal sealed class SplitMix64(ulong seed)
{
    private ulong _state = seed;

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Inteiro em [0, <paramref name="maxExclusive"/>).</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    /// <summary>Inteiro em [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    public void NextBytes(byte[] buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (byte)(NextUInt64() >> 56);
        }
    }
}
