using VNForge.Core.Review;

namespace VNForge.App.Services;

/// <summary>
/// Decide qual troca repetida (<see cref="CorrectionLearner"/>) o app oferece agora como regra: uma por vez, cada uma só uma vez por sessão e por projeto,
/// e nenhuma que já esteja escrita no <c>style.md</c> ou no "Como fala" do personagem. Não chama a IA.
/// </summary>
public sealed class CorrectionOffers
{
    private readonly HashSet<string> offered = new(StringComparer.Ordinal);

    /// <summary>
    /// A primeira troca de <paramref name="found"/> que ainda não foi oferecida nem está escrita, e a marca como oferecida. Uma troca de personagem só entra se
    /// <paramref name="toneOf"/> acha o "Como fala" dele (nulo quando o personagem não está cadastrado no Glossário, e então não há onde gravar).
    /// </summary>
    /// <param name="projectRoot">A pasta do projeto: a mesma regra pode ser oferecida em outro projeto.</param>
    /// <param name="found">As trocas do Core, as mais frequentes primeiro.</param>
    /// <param name="styleGuide">O texto atual do <c>style.md</c>.</param>
    /// <param name="toneOf">O "Como fala" de um falante, ou nulo se o personagem não existe.</param>
    public RepeatedCorrection? Next(string projectRoot, IEnumerable<RepeatedCorrection> found, string? styleGuide, Func<string, string?> toneOf)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(toneOf);
        foreach (var correction in found)
        {
            var key = $"{projectRoot}|{correction.Scope}|{correction.Speaker}|{correction.Swap.From.ToLowerInvariant()}|{correction.Swap.To.ToLowerInvariant()}";
            if (offered.Contains(key) || IsWritten(correction, styleGuide, toneOf, out var hasHome) || !hasHome)
            {
                continue;
            }

            offered.Add(key);
            return correction;
        }

        return null;
    }

    private static bool IsWritten(RepeatedCorrection correction, string? styleGuide, Func<string, string?> toneOf, out bool hasHome)
    {
        if (correction.Scope == CorrectionScope.Project)
        {
            hasHome = true;
            return correction.IsIn(styleGuide);
        }

        var tone = toneOf(correction.Speaker!);
        hasHome = tone is not null;
        return correction.IsIn(tone);
    }
}
