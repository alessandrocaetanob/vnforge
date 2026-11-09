using System.Text;
using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.Core.Bible;

namespace VNForge.AI.Bible;

/// <summary>
/// A bíblia do jogo sobre o Claude. O roteiro do capítulo vai no bloco de sistema que leva o <c>cache_control</c> (ver <see cref="AnthropicMessageClient"/>) e o pedido de
/// cenas vai na mensagem do usuário: as chamadas seguintes sobre o mesmo capítulo reaproveitam o roteiro do cache, a uma fração do preço da entrada. Pede JSON por
/// <c>output_config.format</c> e confere que a resposta traz exatamente as cenas pedidas. Recusa vira <see cref="ClaudeRefusalException"/>; JSON inválido ou incompleto,
/// <see cref="ClaudeResponseException"/>.
/// </summary>
public sealed class ClaudeBibleProvider(IClaudeMessageClient client, ClaudeOptions? options = null) : IBibleProvider
{
    internal const string ScenesSchema = """
        {"type":"object","properties":{"scenes":{"type":"array","items":{"type":"object","properties":{"key":{"type":"string"},"name":{"type":"string"},"summary":{"type":"string"},"characters":{"type":"array","items":{"type":"string"}},"tone":{"type":"array","items":{"type":"string"}},"jokes":{"type":"array","items":{"type":"string"}},"relations":{"type":"array","items":{"type":"object","properties":{"between":{"type":"string"},"note":{"type":"string"}},"required":["between","note"],"additionalProperties":false}}},"required":["key","name","summary","characters","tone","jokes","relations"],"additionalProperties":false}}},"required":["scenes"],"additionalProperties":false}
        """;

    internal const string OverviewSchema = """
        {"type":"object","properties":{"synopsis":{"type":"string"},"timeline":{"type":"array","items":{"type":"object","properties":{"title":{"type":"string"},"note":{"type":"string"}},"required":["title","note"],"additionalProperties":false}}},"required":["synopsis","timeline"],"additionalProperties":false}
        """;

    private const string SceneInstruction = """
        Você ajuda a traduzir visual novels do japonês para o português do Brasil. Antes de traduzir, lê o roteiro de um capítulo e escreve a bíblia das cenas, que a tradução consulta a cada fala.
        Para cada cena pedida, devolva:
        - "key": a chave da cena, exatamente como recebida.
        - "name": um nome curto em português, de até seis palavras, que diga o que acontece ("Atraso no portão"). Sem número de cena.
        - "summary": o resumo em português, de duas a quatro frases: quem está onde, o que acontece e como a cena termina. Não conte o que só aparece em cenas seguintes.
        - "characters": quem aparece na cena, pelo nome da tradução quando a lista de personagens o traz; senão, o nome romanizado.
        - "tone": o tom da cena em poucas palavras cada ("cômico leve", "tenso no fim"), no máximo três.
        - "jokes": piadas, frases ou objetos que voltam em outras cenas e que a tradução precisa manter iguais. Lista vazia quando não há.
        - "relations": como os personagens da cena se tratam; "between" nomeia os dois ("Kenta e Ren") e "note" é uma frase. Lista vazia quando a cena tem um personagem só.
        Escreva só o que o roteiro sustenta e devolva uma entrada por cena pedida. Responda apenas com o JSON pedido.
        """;

    private const string OverviewInstruction = """
        Você ajuda a traduzir visual novels do japonês para o português do Brasil. Recebe, por capítulo, o resumo de cada cena e escreve:
        - "synopsis": a sinopse do jogo em português, de três a cinco frases, sem revelar o final.
        - "timeline": a linha do tempo, uma entrada por capítulo recebido, na ordem recebida; "title" é o nome do capítulo, exatamente como veio, e "note" é uma frase sobre o que acontece nele.
        Responda apenas com o JSON pedido.
        """;

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ClaudeOptions options = options ?? new ClaudeOptions();
    private readonly Lock usageLock = new();
    private ClaudeUsage usage = new(0, 0, 0, 0);

    /// <summary>Tokens acumulados de todas as chamadas deste provedor, com os lidos do cache.</summary>
    public ClaudeUsage Usage
    {
        get { lock (usageLock) return usage; }
    }

    public async Task<IReadOnlyList<BibleScene>> DescribeScenesAsync(BibleChapterInput chapter, IReadOnlyList<string> sceneKeys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(sceneKeys);
        var asked = sceneKeys.Select(k => chapter.Scenes.FirstOrDefault(s => s.Key == k) ?? throw new ArgumentException($"A cena '{k}' não está no capítulo '{chapter.Name}'.", nameof(sceneKeys))).ToList();
        var user = "Descreva estas cenas:\n" + JsonSerializer.Serialize(asked.Select(s => new { key = s.Key, label = s.Label }), PromptJson);
        var text = await CallAsync(ChapterSystem(chapter), user, ScenesSchema, ct).ConfigureAwait(false);
        return ParseScenes(text, sceneKeys);
    }

    public async Task<BibleOverview> SummarizeGameAsync(IReadOnlyList<BibleChapterDigest> chapters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        var user = "Capítulos e resumos das cenas:\n" + JsonSerializer.Serialize(chapters.Select(c => new { chapter = c.Name, scenes = c.Summaries }), PromptJson);
        var text = await CallAsync(OverviewInstruction, user, OverviewSchema, ct).ConfigureAwait(false);
        return ParseOverview(text);
    }

    /// <summary>
    /// O bloco estável do capítulo: a instrução, os nomes dos personagens e o roteiro inteiro. Tem que sair idêntico a cada chamada sobre o capítulo, senão o cache de
    /// prompt não acerta; o que muda de uma chamada para outra (as cenas pedidas) vai na mensagem do usuário.
    /// </summary>
    internal static string ChapterSystem(BibleChapterInput chapter)
    {
        var sb = new StringBuilder(SceneInstruction).Append('\n');
        if (chapter.Characters.Count > 0)
        {
            sb.Append("\nPersonagens (original => tradução):\n");
            foreach (var c in chapter.Characters)
            {
                sb.Append("- ").Append(c.Source).Append(" => ").Append(c.Target).Append('\n');
            }
        }

        return sb.Append("\nCapítulo: ").Append(chapter.Name).Append("\nRoteiro:\n").Append(chapter.Text()).ToString();
    }

    private async Task<string> CallAsync(string system, string user, string schema, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reply = await client.CompleteAsync(new ClaudeRequest(options.Model, options.Effort, options.MaxTokens, system, user, schema), ct).ConfigureAwait(false);

        if (reply.Usage is { } used)
        {
            lock (usageLock)
            {
                usage += used;
            }
        }

        return reply.StopReason switch
        {
            "refusal" => throw new ClaudeRefusalException("A IA não quis ler este capítulo. Tente de novo ou escreva a bíblia à mão.", "stop_reason: refusal"),
            "max_tokens" => throw new ClaudeResponseException("A IA não terminou a resposta. Tente de novo ou escreva a bíblia à mão.", detail: ClaudeResponseException.MaxTokensDetail),
            _ => reply.Text,
        };
    }

    private static List<BibleScene> ParseScenes(string text, IReadOnlyList<string> expected)
    {
        Dictionary<string, BibleScene> byKey = new(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var item in doc.RootElement.GetProperty("scenes").EnumerateArray())
            {
                var scene = new BibleScene(Text(item, "key"))
                {
                    Name = Text(item, "name"),
                    Summary = Text(item, "summary"),
                    Characters = Strings(item, "characters"),
                    Tone = Strings(item, "tone"),
                    Jokes = Strings(item, "jokes"),
                    Relations =
                    [
                        .. item.GetProperty("relations").EnumerateArray()
                            .Select(r => new BibleRelation(Text(r, "between"), Text(r, "note")))
                            .Where(r => r.Between.Length > 0 && r.Note.Length > 0),
                    ],
                };
                if (!byKey.TryAdd(scene.Key, scene))
                {
                    throw Incomplete($"Resposta com a cena '{scene.Key}' repetida.");
                }
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Incomplete("A resposta não é o JSON esperado.", e);
        }

        var wanted = expected.ToHashSet(StringComparer.Ordinal);
        var missing = wanted.Where(k => !byKey.ContainsKey(k)).ToList();
        var extra = byKey.Keys.Where(k => !wanted.Contains(k)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            throw Incomplete($"As cenas da resposta não batem com as pedidas. Faltam: [{string.Join(", ", missing)}]; sobram: [{string.Join(", ", extra)}].");
        }

        var empty = byKey.Values.FirstOrDefault(s => s.Summary.Length == 0);
        return empty is not null ? throw Incomplete($"A cena '{empty.Key}' veio sem resumo.") : [.. expected.Select(k => byKey[k])];
    }

    private static BibleOverview ParseOverview(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var synopsis = Text(doc.RootElement, "synopsis");
            var timeline = doc.RootElement.GetProperty("timeline").EnumerateArray().Select(t => new BibleTimelineEntry(Text(t, "title"), Text(t, "note"))).ToList();
            return synopsis.Length == 0 ? throw Incomplete("A sinopse veio vazia.") : new BibleOverview(synopsis, timeline);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Incomplete("A resposta não é o JSON esperado.", e);
        }
    }

    private static string Text(JsonElement element, string name) =>
        (element.GetProperty(name).GetString() ?? throw Incomplete($"Resposta com o campo '{name}' nulo.")).Trim();

    private static List<string> Strings(JsonElement element, string name) =>
        [.. element.GetProperty(name).EnumerateArray().Select(e => (e.GetString() ?? string.Empty).Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];

    private static ClaudeResponseException Incomplete(string detail, Exception? inner = null) =>
        new("A resposta da IA veio incompleta ou fora do formato. Tente de novo ou escreva a bíblia à mão.", inner, detail);
}
