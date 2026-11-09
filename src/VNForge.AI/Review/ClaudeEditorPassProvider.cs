using System.Text;
using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>
/// A passada de editor sobre o Claude. Faz um pedido por cena (o <see cref="EditorPassRunner"/> só parte uma cena que não cabe num pedido), com a tradução inteira, o glossário,
/// os personagens e o resumo da bíblia, e lê de volta só os ajustes. Usa o mesmo chamador, o mesmo contexto de prompt e o mesmo tratamento de <c>refusal</c> e
/// <c>max_tokens</c> do <see cref="ClaudeReviewProvider"/>; difere em devolver o texto proposto e em ler a cena toda de uma vez, sem refazer linha a linha numa recusa.
/// </summary>
public sealed class ClaudeEditorPassProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : IEditorPassProvider
{
    private readonly ClaudeBatchCaller _caller = new(client, options ?? new ClaudeOptions());

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal const string Schema = """
        {"type":"object","properties":{"adjustments":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"kind":{"type":"string","enum":["fluency","voice","consistency"]},"proposed":{"type":"string"},"reason":{"type":"string"}},"required":["id","kind","proposed","reason"],"additionalProperties":false}}},"required":["adjustments"],"additionalProperties":false}
        """;

    /// <summary>Tokens acumulados de todas as chamadas deste provedor, recusas incluídas.</summary>
    public ClaudeUsage Usage
    {
        get => _caller.Usage;
    }

    /// <summary>Lê a cena e devolve os ajustes. Uma recusa sobe como <see cref="ClaudeRefusalException"/>: a cena fica sem passada, e a tradução dela não muda.</summary>
    public Task<IReadOnlyList<EditorPassProposal>> ReviewSceneAsync(TranslationBatch scene, CancellationToken ct)
    {
        return _caller.RunOnceAsync(scene, new ClaudeBatchSpec<EditorPassProposal>(Schema, "editar", BuildSystem, b => ClaudePromptParts.BatchContext(b, "Glossário (termos obrigatórios):"), BuildUser, Parse, Refused: null), ct);
    }

    private static string BuildSystem(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você é o editor de uma tradução de visual novel do japonês para o português do Brasil. A tradução já está feita; você lê a cena inteira, do começo ao fim, como um leitor.");
        sb.AppendLine("Proponha um ajuste só onde ele melhora a leitura, de um destes tipos:");
        sb.AppendLine("- fluency (fluidez): a frase soa travada, presa à ordem do japonês ou pesada em português.");
        sb.AppendLine("- voice (voz): a fala não soa como o personagem; o registro ou o tratamento destoa do glossário, dos personagens e do tratamento entre eles.");
        sb.AppendLine("- consistency (consistência): o mesmo termo, piada ou tratamento aparece traduzido de outro jeito em outra linha da cena.");
        sb.AppendLine("Para cada ajuste devolva o id da linha, o tipo, a linha inteira como ficaria (não um trecho) e o motivo em uma frase, em português.");
        sb.AppendLine("Deixe de fora as linhas que estão boas; uma cena sem ajuste volta com a lista vazia. No máximo um ajuste por linha.");
        sb.AppendLine("Não mude o sentido nem invente informação, mantenha os marcadores de controle exatamente como estão e respeite o glossário. Responda apenas com o JSON pedido.");
        ClaudePromptParts.AppendStableContext(sb, batch);
        return sb.ToString();
    }

    private static string BuildUser(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        AppendEarlier(sb, batch);
        sb.AppendLine("Leia estas linhas, na ordem da cena, e proponha os ajustes:");
        sb.AppendLine(JsonSerializer.Serialize(
            batch.Lines.Select(u => new { id = u.Id, kind = u.Kind.ToString(), speaker = u.Speaker, text = u.Source, translation = u.Translation }),
            PromptJson));
        return sb.ToString();
    }

    // Uma cena longa vai em mais de um pedido: as falas do pedido anterior entram só para o modelo manter o fio, e não recebem ajuste.
    private static void AppendEarlier(StringBuilder sb, TranslationBatch batch)
    {
        if (batch.PreviousLines.Count == 0)
        {
            return;
        }

        sb.AppendLine("Falas anteriores da mesma cena (contexto, sem ajuste):");
        foreach (var u in batch.PreviousLines)
        {
            sb.AppendLine($"{(u.Speaker is null ? string.Empty : u.Speaker + ": ")}{u.Translation}");
        }

        sb.AppendLine();
    }

    private static IReadOnlyList<EditorPassProposal> Parse(string text, TranslationBatch batch)
    {
        var ids = batch.Lines.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<EditorPassProposal> proposals = [];
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var item in doc.RootElement.GetProperty("adjustments").EnumerateArray())
            {
                // Um ajuste é um extra: o que vier com id que não é do pedido, repetido ou com tipo que não existe é descartado sem derrubar os demais.
                var id = item.GetProperty("id").GetString();
                var proposed = item.GetProperty("proposed").GetString();
                var reason = item.GetProperty("reason").GetString();
                if (id is null || proposed is null || reason is null || !ids.Contains(id) || !TryKind(item.GetProperty("kind").GetString(), out var kind) || !seen.Add(id))
                {
                    continue;
                }

                proposals.Add(new EditorPassProposal(id, kind, proposed, reason));
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ClaudeResponseException("A resposta do Claude não é o JSON esperado.", e);
        }

        return proposals;
    }

    private static bool TryKind(string? name, out EditorSuggestionKind kind)
    {
        kind = name switch
        {
            "fluency" => EditorSuggestionKind.Fluency,
            "voice" => EditorSuggestionKind.Voice,
            "consistency" => EditorSuggestionKind.Consistency,
            _ => (EditorSuggestionKind)(-1),
        };
        return Enum.IsDefined(kind);
    }
}
