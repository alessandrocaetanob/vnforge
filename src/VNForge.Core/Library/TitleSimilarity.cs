using System.Globalization;
using System.Text;

namespace VNForge.Core.Library;

/// <summary>
/// Quanto dois títulos se parecem, de 0 a 1, sem depender do idioma: compara só letras e números (largura cheia vira normal, sem diferenciar maiúsculas) e
/// mede os pares de caracteres em comum, o que serve ao japonês e ao romaji. Um título que contém o outro inteiro pontua pelo tamanho que cobre.
/// </summary>
public static class TitleSimilarity
{
    private const int MinContainedLatin = 3;
    private const int MinContainedCjk = 2;

    /// <summary>O título só com letras e números, em minúsculas e na forma NFKC.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit))
        {
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>A semelhança entre os dois títulos. Textos que normalizam a vazio valem 0.</summary>
    public static double Between(string a, string b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (left == right)
        {
            return 1;
        }

        var (shorter, longer) = left.Length <= right.Length ? (left, right) : (right, left);
        var contained = shorter.Length >= MinimumContained(shorter) && longer.Contains(shorter, StringComparison.Ordinal)
            ? 0.55 + (0.4 * shorter.Length / longer.Length)
            : 0;
        return Math.Max(contained, Dice(left, right));
    }

    private static int MinimumContained(string text) => text.Any(c => c >= '⺀') ? MinContainedCjk : MinContainedLatin;

    private static double Dice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2)
        {
            return 0;
        }

        var pairs = new Dictionary<int, int>();
        for (var i = 0; i < a.Length - 1; i++)
        {
            var key = (a[i] << 16) | a[i + 1];
            pairs[key] = pairs.GetValueOrDefault(key) + 1;
        }

        var shared = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var key = (b[i] << 16) | b[i + 1];
            if (pairs.TryGetValue(key, out var left) && left > 0)
            {
                pairs[key] = left - 1;
                shared++;
            }
        }

        return 2.0 * shared / ((a.Length - 1) + (b.Length - 1));
    }

    /// <summary>Formata a pontuação como porcentagem para a CLI (<c>0,92</c> vira <c>92%</c>), sempre na cultura invariante dos números e com o sinal.</summary>
    public static string Percent(double score) => (score * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
