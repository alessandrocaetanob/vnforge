using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.Core.Factsheet;

namespace VNForge.AI.Sheet;

/// <summary>
/// A tradução da ficha do jogo sobre o Claude. As regras de tradução vão no bloco de sistema, igual a cada chamada (é o que o cache de prompt reaproveita, ver
/// <see cref="AnthropicMessageClient"/>), e os itens do lote vão na mensagem do usuário. Pede JSON por <c>output_config.format</c>. Recusa vira
/// <see cref="ClaudeRefusalException"/>; resposta cortada ou fora do formato, <see cref="ClaudeResponseException"/>. Quem chama (<see cref="SheetTranslationRunner"/>)
/// confere as chaves e os marcadores de spoiler item a item.
/// </summary>
public sealed class ClaudeSheetTranslator(IClaudeMessageClient client, ClaudeOptions? options = null) : ISheetTranslator
{
    internal const string Schema = """
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"key":{"type":"string"},"text":{"type":"string"}},"required":["key","text"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}
        """;

    internal const string Instruction = """
        Você traduz a ficha de uma visual novel do inglês (o texto do VNDB) para o português do Brasil, para um catálogo pessoal. Cada item tem uma "key", um "kind", o "text" a traduzir e, às vezes, um "context".
        Os tipos de item são:
        - "synopsis" e "character_description": texto corrido. Traduza com naturalidade, no tom do original, sem acrescentar nem cortar informação.
        - "tag_name": o nome de uma tag de gênero do VNDB ("Comedy", "Time Travel"). Use o nome que catálogos brasileiros de visual novel usam; o "context" é a categoria da tag (cont: conteúdo, ero: sexual, tech: técnica).
        - "trait_name": o nome de um traço de personagem ("Long Hair", "Tsundere"). Mantenha o termo de gíria de anime quando o público brasileiro o usa assim (tsundere, kuudere); o "context" é o grupo do traço (Hair, Personality...).
        Regras:
        - Mantenha exatamente os marcadores [spoiler] e [/spoiler], nos mesmos lugares do original, e traduza o que está dentro deles. Nunca remova, acrescente nem una trechos de spoiler.
        - Não traduza nomes próprios de pessoas, lugares e do jogo; escreva-os como no original.
        - Não revele nada que o original não diga, e não resuma.
        - Devolva um item por item recebido, com a mesma "key". Responda apenas com o JSON pedido.
        """;

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ClaudeOptions options = options ?? new ClaudeOptions();
    private readonly Lock usageLock = new();
    private ClaudeUsage usage = new(0, 0, 0, 0);

    /// <summary>Tokens acumulados de todas as chamadas deste tradutor, com os lidos do cache.</summary>
    public ClaudeUsage Usage
    {
        get { lock (usageLock) return usage; }
    }

    public async Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var reply = await client.CompleteAsync(new ClaudeRequest(options.Model, options.Effort, options.MaxTokens, Instruction, UserMessage(request), Schema), ct).ConfigureAwait(false);

        if (reply.Usage is { } used)
        {
            lock (usageLock)
            {
                usage += used;
            }
        }

        return reply.StopReason switch
        {
            "refusal" => throw new ClaudeRefusalException("A IA não quis traduzir este texto. Tente de novo, ou escreva a tradução com vnf library sheet edit.", "stop_reason: refusal"),
            "max_tokens" => throw new ClaudeResponseException("A IA não terminou a resposta. Tente de novo; um lote menor ajuda.", detail: ClaudeResponseException.MaxTokensDetail),
            _ => Parse(reply.Text),
        };
    }

    internal static string UserMessage(SheetTranslationRequest request) =>
        $"Jogo: {request.GameTitle}\nItens a traduzir:\n"
        + JsonSerializer.Serialize(request.Items.Select(i => new { key = i.Key, kind = KindName(i.Kind), context = i.Context, text = i.Source }), PromptJson);

    internal static string KindName(SheetItemKind kind) => kind switch
    {
        SheetItemKind.Synopsis => "synopsis",
        SheetItemKind.CharacterDescription => "character_description",
        SheetItemKind.TagName => "tag_name",
        _ => "trait_name",
    };

    private static List<SheetTranslation> Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return
            [
                .. doc.RootElement.GetProperty("items").EnumerateArray().Select(item => new SheetTranslation(
                    item.GetProperty("key").GetString() ?? throw Incomplete("Item sem chave."),
                    item.GetProperty("text").GetString() ?? throw Incomplete("Item sem texto."))),
            ];
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Incomplete("A resposta não é o JSON esperado.", e);
        }
    }

    private static ClaudeResponseException Incomplete(string detail, Exception? inner = null) =>
        new("A resposta da IA veio incompleta ou fora do formato. Tente de novo.", inner, detail);
}
