namespace VNForge.Core.Projects;

/// <summary>Percentuais inteiros por status, na ordem fixa da barra de distribuição. Somam sempre 100.</summary>
public sealed record StatusDistribution(int Approved, int Reviewed, int Translated, int Machine, int Pending)
{
    /// <summary>Divide 100 entre os status pelo método do maior resto; sem linhas, tudo é pendente.</summary>
    public static StatusDistribution Of(StatusCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Total == 0)
        {
            return new StatusDistribution(0, 0, 0, 0, 100);
        }

        int[] values = [counts.Approved, counts.Reviewed, counts.Translated, counts.Machine, counts.Untranslated];
        var shares = new int[values.Length];
        var remainders = new long[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var scaled = 100L * values[i];
            shares[i] = (int)(scaled / counts.Total);
            remainders[i] = scaled % counts.Total;
        }

        var missing = 100 - shares.Sum();
        foreach (var i in Enumerable.Range(0, values.Length).OrderByDescending(i => remainders[i]).ThenByDescending(i => i).Take(missing))
        {
            shares[i]++;
        }

        return new StatusDistribution(shares[0], shares[1], shares[2], shares[3], shares[4]);
    }
}
