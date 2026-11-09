using System.Text;
using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Eval;

/// <summary>
/// Juiz de IA sobre o Claude. Recebe o original e uma tradução e devolve uma nota de 0 a 10 por linha, em JSON por <c>output_config.format</c>. O pedido não leva
/// o modelo, o esforço nem a configuração que gerou o texto, e a tradução aprovada também fica de fora: o juiz avalia o texto contra o original, não contra uma resposta pronta.
/// </summary>
public sealed class ClaudeJudgeProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : IJudgeProvider
{
    private readonly ClaudeBatchCaller _caller = new(client, options ?? new ClaudeOptions { Effort = ClaudeEffort.Low });

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal const string Schema = """
        {"type":"object","properties":{"scores":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"score":{"type":"number"}},"required":["id","score"],"additionalProperties":false}}},"required":["scores"],"additionalProperties":false}
        """;

    public ClaudeUsage? Usage => _caller.Usage;

    public Task<IReadOnlyList<JudgeScore>> JudgeAsync(TranslationBatch batch, CancellationToken ct) =>
        _caller.RunAsync(
            batch,
            new ClaudeBatchSpec<JudgeScore>(Schema, "avaliar", BuildSystem, b => ClaudePromptParts.BatchContext(b, "Glossário (termos obrigatórios):"), BuildUser, Parse, line => new JudgeScore(line.Id, 0, Refused: true)),
            ct);

    private static string BuildSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você avalia traduções de visual novels do japonês para o português do Brasil.");
        sb.AppendLine("Para cada linha, dê uma nota de 0 a 10 à tradução, olhando só o original e o texto traduzido: sentido fiel, naturalidade em português, tom do personagem, glossário e marcadores de controle intactos.");
        sb.AppendLine("10 é uma tradução que um revisor aprovaria sem mexer; 5 é compreensível, mas com erros claros; 0 é errada ou ilegível. Use a escala toda e não dê nota alta por precaução.");
        sb.AppendLine("Devolva uma nota por linha, com o mesmo id. Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendStableContext(sb, batch);
        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Avalie estas traduções:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, original = u.Source, translation = u.Translation }),
            PromptJson));
        return sb.ToString();
    }

    private static IReadOnlyList<JudgeScore> Parse(string text, TranslationBatch batch)
    {
        var byId = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var item in doc.RootElement.GetProperty("scores").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? throw new ClaudeResponseException("Resposta com id nulo.");
                if (!byId.TryAdd(id, Math.Clamp(item.GetProperty("score").GetDouble(), 0, 10)))
                {
                    throw new ClaudeResponseException($"Resposta com id repetido: '{id}'.");
                }
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ClaudeResponseException("A resposta do Claude não é o JSON esperado.", e);
        }

        var expected = batch.Lines.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var extra = byId.Keys.Where(k => !expected.Contains(k)).ToList();
        var missing = expected.Where(k => !byId.ContainsKey(k)).ToList();
        if (extra.Count > 0 || missing.Count > 0)
        {
            throw new ClaudeResponseException($"Ids da resposta não batem com o lote. Faltam: [{string.Join(", ", missing)}]; sobram: [{string.Join(", ", extra)}].");
        }

        return [.. batch.Lines.Select(u => new JudgeScore(u.Id, byId[u.Id]))];
    }
}
