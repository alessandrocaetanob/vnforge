using VNForge.AI.Claude;
using System.Text;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>
/// Estimativa da passada de editor, na mesma heurística de tokens do <see cref="CostEstimator"/>. A entrada é a cena toda (original e tradução de cada linha), as instruções e o contexto
/// do pedido (estilo, guia, sinopse, glossário, personagens, tratamento e resumo da cena na bíblia), que é o que mais pesa em projeto com bíblia;
/// a saída é o raciocínio do modelo e os ajustes, que se supõe cobrirem <see cref="AdjustedShare"/> das linhas. É uma ordem de grandeza: o custo real vem do uso que a API devolve.
/// </summary>
public static class EditorPassCost
{
    /// <summary>A fração das linhas em que se supõe que o editor proponha um ajuste. Chute de partida; o banco de provas (#529) é quem mede.</summary>
    public const double AdjustedShare = 0.2;

    private const double TranslationPerSourceToken = 1.5;
    private const int InstructionTokens = 420;
    private const int PerLineInputOverhead = 24;
    private const int PerAdjustmentOverhead = 45;
    private const int ReasonTokens = 30;

    /// <summary>Os pedidos que a passada faz para as linhas de uma cena: um por fatia de <see cref="EditorPassRunner.MaxLinesPerRequest"/> linhas, com o contexto que <paramref name="builder"/> monta.</summary>
    public static IReadOnlyList<TranslationBatch> Requests(TranslationBatchBuilder builder, IReadOnlyList<TextUnit> lines)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(lines);
        List<TranslationBatch> requests = [];
        for (var start = 0; start < lines.Count; start += EditorPassRunner.MaxLinesPerRequest)
        {
            requests.Add(builder.Build([.. lines.Skip(start).Take(EditorPassRunner.MaxLinesPerRequest)], previous: [.. lines.Take(start)]));
        }

        return requests;
    }

    /// <summary>
    /// O custo de uma passada com os pedidos <paramref name="requests"/> (um por cena, ou por fatia dela; ver <see cref="Requests"/>). Uma linha ainda sem tradução (a estimativa antes de traduzir)
    /// conta com a tradução que o <see cref="CostEstimator"/> espera para ela.
    /// </summary>
    public static CostEstimate Estimate(IReadOnlyList<TranslationBatch> requests, string model, ClaudeEffort effort = ClaudeEffort.Medium)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var price = ModelPricing.For(model);
        long input = 0, output = 0;
        var lineCount = 0;
        var calls = 0;
        foreach (var request in requests.Where(r => r.Lines.Count > 0))
        {
            calls++;
            input += CostEstimator.Tokens(Context(request));
            foreach (var unit in request.Lines)
            {
                var source = CostEstimator.Tokens(unit.Source) + CostEstimator.Tokens(unit.Speaker ?? string.Empty);
                var translation = unit.HasTranslation ? CostEstimator.Tokens(unit.Translation) : (long)Math.Ceiling(source * TranslationPerSourceToken);
                input += source + translation + PerLineInputOverhead;
                output += (long)Math.Ceiling(AdjustedShare * (translation + ReasonTokens + PerAdjustmentOverhead));
                lineCount++;
            }
        }

        input += calls * (long)InstructionTokens;
        output += calls * Reasoning(effort);
        var usage = new TokenUsage(input, output);
        return new CostEstimate(lineCount, 0, usage, lineCount == 0 ? 0m : price.Cost(usage));
    }

    private static string Context(TranslationBatch request)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendStableContext(sb, request);
        sb.Append(ClaudePromptParts.BatchContext(request, "Glossário (use sempre estes termos):"));
        return sb.ToString();
    }

    private static long Reasoning(ClaudeEffort effort) => effort switch
    {
        ClaudeEffort.Low => 200,
        ClaudeEffort.Medium => 800,
        ClaudeEffort.High => 2500,
        _ => 6000,
    };
}
