using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;

namespace VNForge.AI.Explain;

/// <summary>Explica uma fala em japonês para quem não lê japonês. Atrás de uma interface para o Editor rodar nos testes sem rede.</summary>
public interface ILineExplainer
{
    /// <summary>O original quebrado em partes com significado. Vazio quando a fala só tem marcadores.</summary>
    /// <exception cref="ClaudeRefusalException">O modelo recusou explicar a fala.</exception>
    /// <exception cref="ClaudeResponseException">A resposta veio cortada ou fora do formato.</exception>
    Task<LineExplanation> ExplainAsync(string source, CancellationToken ct);
}

/// <summary>
/// "Entender o japonês": pede ao Claude Haiku a quebra do original em partes com significado, em JSON por <c>output_config.format</c>, e guarda a resposta
/// em <c>.cache/explain.json</c> pelo hash do original (<see cref="SourceHash"/>). A explicação é só para ler: nunca entra na tradução.
/// </summary>
public sealed class LineExplainer : ILineExplainer
{
    /// <summary>O uso típico de um pedido (prompt de sistema, a fala e o raciocínio curto), para o preço em reais mostrado antes de pedir.</summary>
    public static TokenUsage TypicalUsage { get; } = new(600, 900);

    public const string Schema = """
        {"type":"object","properties":{"parts":{"type":"array","items":{"type":"object","properties":{"source":{"type":"string"},"meaning":{"type":"string"}},"required":["source","meaning"],"additionalProperties":false}}},"required":["parts"],"additionalProperties":false}
        """;

    private const string SystemPrompt = """
        Você explica falas de visual novels em japonês para quem não lê japonês e vai traduzi-las para o português do Brasil.
        Quebre a fala em partes com significado (palavra, expressão ou oração curta), na ordem em que aparecem no original.
        Para cada parte devolva "source", o trecho exato do original, e "meaning", o significado em português do Brasil em poucas palavras, com o papel gramatical entre parênteses quando ajudar (partícula, forma educada, tema, sujeito).
        Os trechos juntos têm de reproduzir a fala inteira. Não traduza a fala como um todo, não corrija nada e não comente o tom.
        Responda apenas com o JSON pedido.
        """;

    private readonly IClaudeMessageClient client;
    private readonly string cachePath;
    private readonly string model;
    private readonly Lock gate = new();
    private readonly ExplainCache cache;
    private ClaudeUsage usage = new(0, 0, 0, 0);

    /// <param name="client">A fronteira com o SDK; os testes passam um cliente falso.</param>
    /// <param name="cachePath">Caminho de <c>.cache/explain.json</c> do projeto.</param>
    /// <param name="model">O modelo da explicação; o padrão é o Haiku, por ser curta e barata.</param>
    public LineExplainer(IClaudeMessageClient client, string cachePath, string model = ClaudeModels.Haiku)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
        this.model = model;
        cache = ExplainCache.Load(cachePath);
    }

    /// <summary>Tokens gastos por este explicador; respostas do cache não gastam.</summary>
    public ClaudeUsage Usage
    {
        get
        {
            lock (gate)
            {
                return usage;
            }
        }
    }

    /// <summary>O preço de um pedido típico em dólares, para converter em reais com a cotação do usuário.</summary>
    public static decimal EstimateDollars() => ModelPricing.For(ClaudeModels.Haiku).Cost(TypicalUsage);

    public async Task<LineExplanation> ExplainAsync(string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();

        var text = ReadableText(source);
        if (text.Length == 0)
        {
            return new LineExplanation([], model, FromCache: false);
        }

        var hash = SourceHash.Of(source);
        if (cache.TryGet(hash, out var cached))
        {
            return cached with { FromCache = true };
        }

        var request = new ClaudeRequest(model, ClaudeEffort.Low, 4000, SystemPrompt, JsonSerializer.Serialize(new { text }, PromptJson), Schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);
        if (reply.Usage is { } used)
        {
            lock (gate)
            {
                usage += used;
            }
        }

        if (reply.StopReason == "refusal")
        {
            throw new ClaudeRefusalException("A IA recusou explicar esta fala.");
        }

        if (reply.StopReason == "max_tokens")
        {
            throw new ClaudeResponseException("A explicação foi cortada por limite de tokens; tente de novo.");
        }

        var explanation = new LineExplanation(Parse(reply.Text), model, FromCache: false);
        cache.Set(hash, explanation);
        cache.Save(cachePath);
        return explanation;
    }

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A fala sem os marcadores do jogo, que não são japonês; uma fala fora da gramática dos marcadores segue como está.</summary>
    private static string ReadableText(string source)
    {
        try
        {
            return string.Concat(ControlMarkers.Split(source).Where(s => !s.IsMarker).Select(s => s.Text)).Trim();
        }
        catch (ArgumentException)
        {
            return source.Trim();
        }
    }

    private static List<ExplainedPart> Parse(string text)
    {
        var list = new List<ExplainedPart>();
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                list.AddRange(parts.EnumerateArray().Select(ReadPart).OfType<ExplainedPart>());
            }
        }
        catch (JsonException e)
        {
            throw new ClaudeResponseException("A IA não devolveu a explicação no formato esperado.", e);
        }

        return list.Count > 0 ? list : throw new ClaudeResponseException("A IA não devolveu a explicação no formato esperado.");
    }

    private static ExplainedPart? ReadPart(JsonElement part)
    {
        var source = Text(part, "source");
        var meaning = Text(part, "meaning");
        return string.IsNullOrEmpty(source) || string.IsNullOrEmpty(meaning) ? null : new ExplainedPart(source, meaning);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
}
