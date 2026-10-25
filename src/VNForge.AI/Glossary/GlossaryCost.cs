using VNForge.AI.Cost;

namespace VNForge.AI.Glossary;

/// <summary>
/// Estimativa do que as sugestões do Glossário custam, antes de a pessoa pedir. Mesma heurística do <see cref="CostEstimator"/>
/// (japonês conta 1 token por caractere, ASCII 1 a cada 4); o custo real vem do uso que a API devolve. O valor em reais sai de
/// <see cref="CostEstimator.Brl"/> com a cotação da pessoa.
/// </summary>
public static class GlossaryCost
{
    private const int SystemOverhead = 350;
    private const int PerItemOverhead = 25;
    private const int VoiceOutputTokens = 45;
    private const int TermOutputTokens = 60;

    public static CostEstimate Voices(IReadOnlyList<CharacterVoiceRequest> characters, string model)
    {
        ArgumentNullException.ThrowIfNull(characters);
        long input = 0;
        foreach (var c in characters)
        {
            input += CostEstimator.Tokens(c.Name) + PerItemOverhead + c.Lines.Sum(CostEstimator.Tokens);
        }

        return Estimate(characters.Count, input, characters.Count * (long)VoiceOutputTokens, model);
    }

    public static CostEstimate Terms(IReadOnlyList<TermTranslationRequest> terms, string model)
    {
        ArgumentNullException.ThrowIfNull(terms);
        long input = 0;
        foreach (var t in terms)
        {
            input += CostEstimator.Tokens(t.Source) + CostEstimator.Tokens(t.Example ?? string.Empty) + PerItemOverhead;
        }

        return Estimate(terms.Count, input, terms.Count * (long)TermOutputTokens, model);
    }

    private static CostEstimate Estimate(int items, long input, long output, string model)
    {
        if (items == 0)
        {
            return new CostEstimate(0, 0, default, 0m);
        }

        var usage = new TokenUsage(input + SystemOverhead, output);
        return new CostEstimate(items, 0, usage, ModelPricing.For(model).Cost(usage));
    }
}
