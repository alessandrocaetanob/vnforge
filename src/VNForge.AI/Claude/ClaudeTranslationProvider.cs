using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Claude;

/// <summary>
/// Provedor de tradução sobre o Claude. Monta o pedido, exige saída JSON por <c>output_config.format</c> e confere que
/// a resposta traz exatamente os ids do lote. Retry, reenvio de linhas ruins e fallback de recusa ficam acima dele.
/// </summary>
public sealed class ClaudeTranslationProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : ITranslationProvider
{
    private readonly ClaudeBatchCaller _caller = new(client, options ?? new ClaudeOptions());

    // Sem escape de não-ASCII: o japonês chega legível ao modelo e gasta menos tokens.
    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal const string Schema = """
        {"type":"object","properties":{"translations":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"translation":{"type":"string"}},"required":["id","translation"],"additionalProperties":false}}},"required":["translations"],"additionalProperties":false}
        """;

    /// <summary>Tokens acumulados de todas as chamadas deste provedor, recusas incluídas, com os lidos do cache.</summary>
    public ClaudeUsage Usage
    {
        get => _caller.Usage;
    }

    /// <summary>
    /// Traduz o lote. Uma recusa que sobrevive ao fallback do servidor não derruba o lote: ele é refeito linha a linha
    /// e só as linhas recusadas voltam com <see cref="TranslationResult.Refused"/>.
    /// </summary>
    public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        return _caller.RunAsync(batch, new ClaudeBatchSpec<TranslationResult>(Schema, "traduzir", BuildSystem, b => ClaudePromptParts.BatchContext(b, "Glossário (use sempre estes termos):"), BuildUser, Parse, line => new TranslationResult(line.Id, string.Empty, Refused: true)), ct);
    }

    private static string BuildSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você traduz linhas de visual novels do japonês para o português do Brasil.");
        sb.AppendLine("Mantenha exatamente os marcadores de controle do original e devolva uma tradução por linha, com o mesmo id.");
        sb.AppendLine("Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendStableContext(sb, batch);
        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendPrevious(sb, batch, "Contexto anterior (não traduzir):");
        ClaudePromptParts.AppendNext(sb, batch, "Contexto seguinte (não traduzir):");
        sb.AppendLine("Traduza estas linhas:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<TranslationResult> Parse(string text, TranslationBatch batch)
    {
        var translations = ClaudePromptParts.ParseValues(text, "translations", "translation", batch);
        return [.. batch.Lines.Select((u, i) => new TranslationResult(u.Id, translations[i]))];
    }
}
