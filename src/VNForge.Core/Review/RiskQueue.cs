using System.Globalization;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Review;

/// <summary>Quanto da fila é de cada nível, em percentuais inteiros que somam 100.</summary>
public sealed record RiskDistribution(int High, int Medium, int Low)
{
    /// <summary>Divide 100 entre os níveis pelo método do maior resto; uma fila vazia é toda baixa.</summary>
    public static RiskDistribution Of(int high, int medium, int low)
    {
        var total = high + medium + low;
        if (total == 0)
        {
            return new RiskDistribution(0, 0, 100);
        }

        int[] values = [high, medium, low];
        var shares = new int[values.Length];
        var remainders = new long[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var scaled = 100L * values[i];
            shares[i] = (int)(scaled / total);
            remainders[i] = scaled % total;
        }

        foreach (var i in Enumerable.Range(0, values.Length).OrderByDescending(i => remainders[i]).ThenByDescending(i => i).Take(100 - shares.Sum()))
        {
            shares[i]++;
        }

        return new RiskDistribution(shares[0], shares[1], shares[2]);
    }
}

/// <summary>
/// A fila "o que mais precisa de você": as linhas que esperam revisão (traduzidas pela IA ou pelo tradutor, ainda sem aprovação), do maior risco ao menor
/// (<see cref="LineRisk"/>). O empate fica na ordem da história: os scripts em ordem ordinal da chave e, dentro de cada um, a ordem do arquivo.
/// </summary>
public sealed class RiskQueue
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly int high;
    private readonly int medium;

    private RiskQueue(IReadOnlyList<LineRisk> lines)
    {
        Lines = lines;
        high = lines.Count(l => l.Level == RiskLevel.High);
        medium = lines.Count(l => l.Level == RiskLevel.Medium);
        Distribution = RiskDistribution.Of(high, medium, lines.Count - high - medium);
    }

    public IReadOnlyList<LineRisk> Lines { get; }

    public RiskDistribution Distribution { get; }

    /// <summary>Monta a fila das linhas do projeto. Sem <paramref name="suggestions"/>, a sugestão do editor não pesa.</summary>
    public static RiskQueue Build(ProjectLines lines, PendingEditorSuggestions? suggestions = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var scored = new List<LineRisk>();
        foreach (var script in lines.Scripts)
        {
            foreach (var line in lines.LinesOf(script.Key))
            {
                if (line.Unit.Status is TranslationStatus.Machine or TranslationStatus.Translated)
                {
                    scored.Add(LineRisk.Of(line, suggestions?.Covers(line.ScriptKey, line.Unit) == true));
                }
            }
        }

        return new RiskQueue([.. scored.OrderByDescending(l => l.Score)]);
    }

    public int Count(RiskLevel level) => level switch
    {
        RiskLevel.High => high,
        RiskLevel.Medium => medium,
        _ => Lines.Count - high - medium,
    };

    /// <summary>
    /// O tempo para revisar <paramref name="lines"/> no ritmo da pessoa, arredondado para cima ao minuto; zero sem linhas e nulo enquanto não há ritmo
    /// (<see cref="ReviewPace.PerHour"/>).
    /// </summary>
    public static TimeSpan? TimeToReview(int lines, ReviewPace pace)
    {
        ArgumentNullException.ThrowIfNull(pace);
        if (lines <= 0)
        {
            return TimeSpan.Zero;
        }

        return pace.PerHour is { } perHour and > 0 ? TimeSpan.FromMinutes(Math.Ceiling(60.0 * lines / perHour)) : null;
    }

    /// <summary>
    /// A frase das horas que faltam para o risco alto, a mesma na tela e no <c>vnf status --risk</c>: "312 linhas de risco alto · faltam cerca de 3 h 43 min no seu ritmo de 84 linhas por hora".
    /// Sem ritmo diz que ainda não há; sem linhas de risco alto não há horas a dizer.
    /// </summary>
    /// <param name="high">As linhas de risco alto que faltam revisar.</param>
    public static string DescribeHigh(int high, ReviewPace pace)
    {
        ArgumentNullException.ThrowIfNull(pace);
        if (high <= 0)
        {
            return "Nenhuma linha de risco alto";
        }

        var count = $"{high.ToString("N0", PortugueseBrazil)} {(high == 1 ? "linha" : "linhas")} de risco alto";
        if (TimeToReview(high, pace) is not { } time || pace.PerHour is not { } perHour)
        {
            return $"{count} · sem ritmo ainda, aprove algumas linhas para calcular as horas";
        }

        return $"{count} · faltam cerca de {FormatTime(time)} no seu ritmo de {perHour.ToString("N0", PortugueseBrazil)} {(perHour == 1 ? "linha" : "linhas")} por hora";
    }

    /// <summary>"3 h 43 min", "45 min", "2 h" ou "0 min".</summary>
    public static string FormatTime(TimeSpan time)
    {
        var minutes = (int)Math.Ceiling(time.TotalMinutes);
        var hours = minutes / 60;
        var rest = minutes % 60;
        return (hours, rest) switch
        {
            (0, _) => $"{rest} min",
            (_, 0) => $"{hours} h",
            _ => $"{hours} h {rest} min",
        };
    }
}
