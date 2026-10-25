using System.Text;
using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>
/// Provedor de revisão sobre o Claude. Pede, por linha, uma sugestão curta ou vazio quando a tradução está boa, em JSON por
/// <c>output_config.format</c>, e confere que a resposta traz exatamente os ids do lote. Retry fica no <see cref="TranslationRunner"/>.
/// </summary>
public sealed class ClaudeReviewProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : IReviewProvider
{
    private readonly ClaudeBatchCaller _caller = new(client, options ?? new ClaudeOptions());

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal const string Schema = """
        {"type":"object","properties":{"reviews":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"suggestion":{"type":"string"}},"required":["id","suggestion"],"additionalProperties":false}}},"required":["reviews"],"additionalProperties":false}
        """;

    /// <summary>Tokens acumulados de todas as chamadas deste provedor, recusas incluídas.</summary>
    public ClaudeUsage Usage
    {
        get => _caller.Usage;
    }

    /// <summary>Revisa o lote. Uma recusa refaz o lote linha a linha, e só as linhas recusadas voltam com <see cref="ReviewResult.Refused"/>.</summary>
    public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct)
    {
        return _caller.RunAsync(batch, new ClaudeBatchSpec<ReviewResult>(Schema, "revisar", BuildSystem, b => ClaudePromptParts.BatchContext(b, "Glossário (termos obrigatórios):"), BuildUser, Parse, line => new ReviewResult(line.Id, string.Empty, Refused: true)), ct);
    }

    private static string BuildSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você revisa traduções de visual novels do japonês para o português do Brasil.");
        sb.AppendLine("Para cada linha, confira sentido, naturalidade, tom do personagem, glossário e marcadores de controle.");
        sb.AppendLine("Devolva uma sugestão curta (uma ou duas frases, em português) por linha, com o mesmo id; se a tradução está boa, devolva a sugestão vazia.");
        sb.AppendLine("Não reescreva a tradução inteira nem altere os marcadores. Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendStableContext(sb, batch);
        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendPrevious(sb, batch, "Contexto anterior (não revisar):");
        sb.AppendLine("Revise estas linhas:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source, translation = u.Translation }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<ReviewResult> Parse(string text, TranslationBatch batch)
    {
        var suggestions = ClaudePromptParts.ParseValues(text, "reviews", "suggestion", batch);
        return [.. batch.Lines.Select((u, i) => new ReviewResult(u.Id, suggestions[i].Trim()))];
    }
}
