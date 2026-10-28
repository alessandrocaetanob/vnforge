using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;

namespace VNForge.AI.Claude;

/// <summary>A reescrita dirigida de uma fala ("Mais curta", "Mais natural"...): duas opções por pedido, pelo mesmo cliente e o mesmo tratamento de recusa da tradução.</summary>
public sealed partial class ClaudeTranslationProvider : IRewriteProvider
{
    internal const string RewriteSchema = """
        {"type":"object","properties":{"rewrites":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"optionA":{"type":"string"},"optionB":{"type":"string"}},"required":["id","optionA","optionB"],"additionalProperties":false}}},"required":["rewrites"],"additionalProperties":false}
        """;

    /// <summary>
    /// Pede duas versões da tradução de hoje na direção pedida. Uma recusa que sobrevive ao fallback do servidor volta como
    /// <see cref="RewriteResult.Refused"/>, sem derrubar quem chamou.
    /// </summary>
    public async Task<RewriteResult> RewriteAsync(TranslationBatch batch, RewriteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(request);
        if (batch.Lines.Count != 1)
            throw new ArgumentException("A reescrita trabalha com uma linha por vez.", nameof(batch));

        var spec = new ClaudeBatchSpec<RewriteResult>(
            RewriteSchema,
            "reescrever",
            BuildRewriteSystem,
            b => RewriteBatchContext(b, request),
            b => BuildRewriteUser(b, request),
            ParseRewrite,
            line => new RewriteResult(line.Id, [], Refused: true),
            "A resposta da IA foi cortada por limite de tokens. Tente de novo ou edite o texto à mão.");
        var results = await _caller.RunAsync(batch, spec, ct).ConfigureAwait(false);
        return results[0];
    }

    private static string BuildRewriteSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você reescreve a tradução de uma fala de visual novel do japonês para o português do Brasil.");
        sb.AppendLine("A linha traz o original (\"text\") e a tradução de hoje (\"current\"). Devolva duas versões novas da tradução, diferentes entre si e da de hoje, em \"optionA\" e \"optionB\", com o mesmo id.");
        sb.AppendLine("Mantenha exatamente os marcadores de controle do original e o sentido da fala. Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendStableContext(sb, batch);
        return sb.ToString();
    }

    private static string RewriteBatchContext(TranslationBatch batch, RewriteRequest request)
    {
        var context = ClaudePromptParts.BatchContext(batch, "Glossário (use sempre estes termos):");
        var instruction = new StringBuilder(DirectionText(request.Direction));
        if (request.MaxLines is { } maxLines && request.MaxWidth is { } maxWidth)
            instruction.Append($" Cada versão tem que caber na caixa do jogo: no máximo {maxLines} linhas de {maxWidth} colunas.");

        return context.Length == 0
            ? instruction + Environment.NewLine
            : context + Environment.NewLine + instruction + Environment.NewLine;
    }

    private static string DirectionText(RewriteDirection direction) => direction switch
    {
        RewriteDirection.Shorter => "Direção: mais curta. Diga o mesmo com menos palavras, no mesmo tom.",
        RewriteDirection.MoreNatural => "Direção: mais natural. Escreva como um brasileiro diria a fala em voz alta, sem soar traduzido.",
        RewriteDirection.MoreFaithful => "Direção: mais fiel. Aproxime-se do que o original diz e de como ele diz, sem inventar nem omitir nada.",
        RewriteDirection.MoreFormal => "Direção: mais formal. Use um registro polido e respeitoso, sem mudar o que a fala diz.",
        RewriteDirection.AsCharacter => "Direção: como o personagem falaria. Use o tom de fala (\"tom\") do falante, na lista de personagens, em vocabulário, ritmo e jeito de tratar o outro.",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    private static string BuildRewriteUser(TranslationBatch batch, RewriteRequest request)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendPrevious(sb, batch, "Contexto anterior (não reescrever):");
        ClaudePromptParts.AppendNext(sb, batch, "Contexto seguinte (não reescrever):");
        sb.AppendLine("Reescreva esta linha:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source, current = request.Current }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<RewriteResult> ParseRewrite(string text, TranslationBatch batch)
    {
        var first = ClaudePromptParts.ParseValues(text, "rewrites", "optionA", batch);
        var second = ClaudePromptParts.ParseValues(text, "rewrites", "optionB", batch);
        return [.. batch.Lines.Select((u, i) => new RewriteResult(u.Id, [.. new[] { first[i], second[i] }.Select(o => o.Trim()).Where(o => o.Length > 0)]))];
    }
}
