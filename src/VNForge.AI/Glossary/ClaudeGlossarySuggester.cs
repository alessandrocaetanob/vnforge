using System.Text;
using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.Core.Glossary;

namespace VNForge.AI.Glossary;

/// <summary>
/// Sugestões do Glossário sobre o Claude (Haiku por padrão): voz e gênero a partir das falas, e a tradução dos termos que se repetem com o
/// motivo em uma frase. Pede JSON por <c>output_config.format</c>, divide pedidos grandes em blocos e confere que a resposta traz exatamente
/// os nomes (ou termos) pedidos. Recusa vira <see cref="ClaudeRefusalException"/>; JSON inválido ou incompleto, <see cref="ClaudeResponseException"/>.
/// </summary>
public sealed class ClaudeGlossarySuggester(IClaudeMessageClient client, ClaudeOptions? options = null) : IGlossarySuggester
{
    /// <summary>Personagens por chamada.</summary>
    public const int VoiceChunk = 20;

    /// <summary>Termos por chamada.</summary>
    public const int TermChunk = 30;

    internal const string VoiceSchema = """
        {"type":"object","properties":{"characters":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"gender":{"type":"string","enum":["female","male","other","unknown"]},"voice":{"type":"string"}},"required":["name","gender","voice"],"additionalProperties":false}}},"required":["characters"],"additionalProperties":false}
        """;

    internal const string TermSchema = """
        {"type":"object","properties":{"terms":{"type":"array","items":{"type":"object","properties":{"source":{"type":"string"},"target":{"type":"string"},"reason":{"type":"string"}},"required":["source","target","reason"],"additionalProperties":false}}},"required":["terms"],"additionalProperties":false}
        """;

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ClaudeOptions options = options ?? new ClaudeOptions { Model = ClaudeModels.Haiku, Effort = ClaudeEffort.Low, MaxTokens = 8000 };
    private readonly Lock usageLock = new();
    private ClaudeUsage usage = new(0, 0, 0, 0);

    /// <summary>Tokens acumulados de todas as chamadas deste sugestor.</summary>
    public ClaudeUsage Usage
    {
        get { lock (usageLock) return usage; }
    }

    public async Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(characters);
        List<CharacterVoiceSuggestion> all = [];
        foreach (var chunk in characters.Chunk(VoiceChunk))
        {
            var text = await CallAsync(VoiceSystem, VoiceUser(chunk), VoiceSchema, ct).ConfigureAwait(false);
            all.AddRange(ParseVoices(text, chunk));
        }

        return all;
    }

    public async Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(terms);
        List<TermTranslationSuggestion> all = [];
        foreach (var chunk in terms.Chunk(TermChunk))
        {
            var text = await CallAsync(TermSystem, TermUser(chunk), TermSchema, ct).ConfigureAwait(false);
            all.AddRange(ParseTerms(text, chunk));
        }

        return all;
    }

    private async Task<string> CallAsync(string system, string user, string schema, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var request = new ClaudeRequest(options.Model, options.Effort, options.MaxTokens, system, user, schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

        if (reply.Usage is { } used)
        {
            lock (usageLock)
            {
                usage += used;
            }
        }

        return reply.StopReason switch
        {
            "refusal" => throw new ClaudeRefusalException("O modelo recusou fazer esta sugestão (stop_reason: refusal)."),
            "max_tokens" => throw new ClaudeResponseException("A resposta foi cortada por limite de tokens; peça menos itens por vez."),
            _ => reply.Text,
        };
    }

    private const string VoiceSystem = """
        Você ajuda a traduzir visual novels do japonês para o português do Brasil.
        Para cada personagem, leia as falas e devolva o gênero (female, male, other ou unknown, se as falas não deixam claro)
        e, em "voice", uma frase curta em português dizendo como ele fala (por exemplo "animada, trata o veterano com respeito").
        Use "other" para narrador, grupo ou personagem sem gênero. Não invente: na dúvida, "unknown".
        Devolva um item por personagem, com o mesmo "name" recebido. Responda apenas com o JSON pedido.
        """;

    private const string TermSystem = """
        Você ajuda a traduzir visual novels do japonês para o português do Brasil.
        Cada termo é uma sequência de kanji ou katakana que se repete no jogo. Para cada um, devolva a tradução mais natural em "target"
        e, em "reason", uma frase em português explicando a escolha (nome próprio, gíria, título, termo do enredo).
        Mantenha nomes próprios na grafia usual (Hepburn). Devolva um item por termo, com o mesmo "source" recebido. Responda apenas com o JSON pedido.
        """;

    private static string VoiceUser(CharacterVoiceRequest[] chunk) =>
        "Personagens e falas:\n" + JsonSerializer.Serialize(chunk.Select(c => new { name = c.Name, lines = c.Lines }), PromptJson);

    private static string TermUser(TermTranslationRequest[] chunk) =>
        "Termos:\n" + JsonSerializer.Serialize(chunk.Select(t => new { source = t.Source, lines = t.Lines, example = t.Example }), PromptJson);

    private static List<CharacterVoiceSuggestion> ParseVoices(string text, CharacterVoiceRequest[] chunk)
    {
        var items = ReadItems(text, "characters", "name", "gender", "voice");
        var byName = Index(items, chunk.Select(c => c.Name), "personagens");
        return [.. chunk.Select(c => ToVoice(c.Name, byName[c.Name]))];
    }

    private static CharacterVoiceSuggestion ToVoice(string name, string[] fields) =>
        new(name, ParseGender(fields[1]), fields[2].Trim());

    private static CharacterGender ParseGender(string value) => value switch
    {
        "female" => CharacterGender.Female,
        "male" => CharacterGender.Male,
        "other" => CharacterGender.Other,
        "unknown" => CharacterGender.Unknown,
        _ => throw new ClaudeResponseException($"Gênero desconhecido na resposta: '{value}'."),
    };

    private static List<TermTranslationSuggestion> ParseTerms(string text, TermTranslationRequest[] chunk)
    {
        var items = ReadItems(text, "terms", "source", "target", "reason");
        var bySource = Index(items, chunk.Select(t => t.Source), "termos");
        return [.. chunk.Select(t => new TermTranslationSuggestion(t.Source, bySource[t.Source][1].Trim(), bySource[t.Source][2].Trim()))];
    }

    /// <summary>Lê <c>{ "&lt;array&gt;": [ { "&lt;campo&gt;": "texto", ... } ] }</c> e devolve os campos de cada item, na ordem pedida.</summary>
    private static List<string[]> ReadItems(string text, string arrayName, params string[] fields)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            List<string[]> items = [];
            foreach (var item in doc.RootElement.GetProperty(arrayName).EnumerateArray())
            {
                items.Add([.. fields.Select(f => item.GetProperty(f).GetString() ?? throw new ClaudeResponseException($"Resposta com o campo '{f}' vazio."))]);
            }

            return items;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ClaudeResponseException("A resposta do Claude não é o JSON esperado.", e);
        }
    }

    /// <summary>Exige exatamente as chaves pedidas, sem repetição, e as indexa pelo primeiro campo.</summary>
    private static Dictionary<string, string[]> Index(List<string[]> items, IEnumerable<string> expected, string what)
    {
        Dictionary<string, string[]> byKey = new(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!byKey.TryAdd(item[0], item))
            {
                throw new ClaudeResponseException($"Resposta com {what} repetidos: '{item[0]}'.");
            }
        }

        var wanted = expected.ToHashSet(StringComparer.Ordinal);
        var missing = wanted.Where(k => !byKey.ContainsKey(k)).ToList();
        var extra = byKey.Keys.Where(k => !wanted.Contains(k)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            var sb = new StringBuilder($"A resposta não bate com os {what} pedidos.");
            sb.Append($" Faltam: [{string.Join(", ", missing)}]; sobram: [{string.Join(", ", extra)}].");
            throw new ClaudeResponseException(sb.ToString());
        }

        return byKey;
    }
}
