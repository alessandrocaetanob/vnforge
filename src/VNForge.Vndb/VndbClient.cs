using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>
/// O cliente da API Kana do VNDB (<c>api.vndb.org/kana</c>). Todo pedido passa pela <see cref="VndbRequestQueue"/>, que o mantém dentro do limite da API, e as consultas de catálogo
/// passam pelo <see cref="VndbCache"/>. O token da conta só entra como parâmetro dos métodos de lista e vai no cabeçalho <c>Authorization</c>: não é guardado no cliente, no cache nem no log.
/// O limite do VNDB vale por endereço IP e não por cliente, então todos os clientes do processo devem dividir a mesma fila: <see cref="Create"/> faz isso.
/// </summary>
public sealed class VndbClient : IVndbCatalog, IVndbUserList
{
    public static readonly Uri DefaultBaseUri = new("https://api.vndb.org/kana/");

    /// <summary>Quanto o cliente espera por um pedido inteiro, resposta lida até o fim, antes de desistir.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const string VisualNovelFields =
        "title,alttitle,titles.lang,titles.title,titles.latin,titles.official,titles.main,aliases,olang,devstatus,released,languages,platforms,"
        + "image.url,image.thumbnail,image.dims,image.sexual,image.violence,length,length_minutes,length_votes,rating,description,"
        + "tags.id,tags.name,tags.category,tags.rating,tags.spoiler,tags.lie";

    private const string CharacterFields =
        "name,original,aliases,description,gender,image.url,image.dims,image.sexual,image.violence,"
        + "vns.id,vns.role,vns.spoiler,vns.release.id,traits.id,traits.name,traits.group_id,traits.group_name,traits.spoiler,traits.lie";

    private const string ReleaseFields =
        "title,alttitle,languages.lang,languages.title,languages.latin,languages.mtl,languages.main,platforms,released,minage,patch,freeware,official,engine,voiced,vns.id,vns.rtype";

    private const string ListFields = "id,added,voted,lastmod,vote,started,finished,notes,labels.id,labels.label,vn.title";

    private const int PageSize = 100;
    private const int MaxCharacterPages = 10;
    private const int MaxReleasePages = 3;
    private const int MaxSearchCharacterPages = 5;
    private const int MaxListPages = 200;

    /// <summary>
    /// A espera, sem <c>Retry-After</c>, antes de cada nova tentativa depois de um limite de uso: o VNDB não manda o cabeçalho no 429 de tempo de execução e o bloqueio
    /// pode durar uns 180 s. Esgotadas as esperas, o erro sobe.
    /// </summary>
    internal static readonly TimeSpan[] ThrottleWaits = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(180)];

    private static readonly Lazy<VndbRequestQueue> SharedQueue = new(() => new VndbRequestQueue(TimeProvider.System));
    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    private readonly HttpClient _http;
    private readonly VndbRequestQueue _queue;
    private readonly VndbCache? _cache;
    private readonly Uri _baseUri;
    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;

    public VndbClient(HttpClient http, VndbRequestQueue queue, VndbCache? cache = null, Uri? baseUri = null, TimeProvider? time = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(queue);
        _http = http;
        _queue = queue;
        _cache = cache;
        _baseUri = EnsureTrailingSlash(baseUri ?? DefaultBaseUri);
        _time = time ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>A fila e o <see cref="HttpClient"/> que todos os clientes criados por <see cref="Create"/> dividem no processo, porque o limite do VNDB é por IP.</summary>
    public static VndbRequestQueue SharedRequestQueue => SharedQueue.Value;

    internal VndbRequestQueue Queue => _queue;

    /// <summary>
    /// Um cliente pronto para o endpoint de produção, com o cache na pasta dada (sem pasta, não há cache em disco). Sem <paramref name="queue"/>, usa a fila do processo
    /// (<see cref="SharedRequestQueue"/>) e o <see cref="HttpClient"/> compartilhado, para dois clientes juntos não passarem de 200 pedidos a cada 5 minutos.
    /// Passar <paramref name="time"/> sem <paramref name="queue"/> cria uma fila própria com esse relógio, o que só interessa a teste.
    /// </summary>
    public static VndbClient Create(string? cacheDirectory = null, TimeProvider? time = null, VndbRequestQueue? queue = null)
    {
        queue ??= time is null ? SharedQueue.Value : new VndbRequestQueue(time);
        time ??= TimeProvider.System;
        return new VndbClient(SharedHttp.Value, queue, cacheDirectory is null ? null : new VndbCache(cacheDirectory, time), time: time);
    }

    public async Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default)
    {
        var text = VndbRules.RequireTitle(title);
        var query = Query(new JsonArray("search", "=", text), VisualNovelFields, "searchrank", Math.Clamp(limit, 1, PageSize), 1);
        var page = await QueryPageAsync<VndbWire.VisualNovel>("vn", query, ct).ConfigureAwait(false);
        return [.. page.Results.Select(VndbWire.ToCore)];
    }

    public async Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default)
    {
        VndbRules.RequireId(id, 'v');
        var query = Query(new JsonArray("id", "=", id), VisualNovelFields, null, 1, 1);
        var page = await QueryPageAsync<VndbWire.VisualNovel>("vn", query, ct).ConfigureAwait(false);
        return page.Results.Count == 0 ? null : VndbWire.ToCore(page.Results[0]);
    }

    public async Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default)
    {
        VndbRules.RequireId(visualNovelId, 'v');
        var (results, truncated) = await QueryAllAsync<VndbWire.Character>(
            "character",
            () => new JsonArray("vn", "=", new JsonArray("id", "=", visualNovelId)),
            CharacterFields,
            null,
            MaxCharacterPages,
            ct).ConfigureAwait(false);
        return new VndbResults<VndbCharacter>([.. results.Select(VndbWire.ToCore)], truncated);
    }

    public async Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default)
    {
        var text = VndbRules.RequireTitle(title);
        var (results, truncated) = await QueryAllAsync<VndbWire.Character>(
            "character",
            () => new JsonArray("vn", "=", new JsonArray("search", "=", text)),
            CharacterFields,
            null,
            MaxSearchCharacterPages,
            ct).ConfigureAwait(false);
        return new VndbResults<VndbCharacter>([.. results.Select(VndbWire.ToCore)], truncated);
    }

    public async Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default)
    {
        VndbRules.RequireId(visualNovelId, 'v');
        var (results, truncated) = await QueryAllAsync<VndbWire.Release>(
            "release",
            () => new JsonArray("vn", "=", new JsonArray("id", "=", visualNovelId)),
            ReleaseFields,
            "released",
            MaxReleasePages,
            ct).ConfigureAwait(false);
        return new VndbResults<VndbRelease>([.. results.Select(VndbWire.ToCore)], truncated);
    }

    /// <summary>
    /// <c>GET /authinfo</c> (api.vndb.org/kana, seção "GET /authinfo"): valida o token e devolve o <c>id</c>, o <c>username</c> e as <c>permissions</c>
    /// (<c>listread</c> e <c>listwrite</c>). Token inválido é 401 (seção "HTTP Response Codes").
    /// </summary>
    public async Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default)
    {
        var text = await SendAsync(HttpMethod.Get, "authinfo", null, RequireToken(token), ct).ConfigureAwait(false);
        var info = Parse<VndbWire.AuthInfo>(text);
        return new VndbAuthInfo(info.Id, info.Username, [.. info.Permissions ?? []]);
    }

    /// <summary>
    /// <c>POST /ulist</c> (seção "List Management > POST /ulist"): <c>user</c> é obrigatório e os campos <c>id, added, voted, lastmod, vote, started, finished, notes,
    /// labels.id, labels.label</c> são os da seção "Fields". Rótulos privados só vêm com o token (permissão <c>listread</c>).
    /// </summary>
    public async Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default)
    {
        VndbRules.RequireId(userId, 'u');
        var authorization = token is null ? null : RequireToken(token);
        var entries = new List<VndbListEntry>();
        var truncated = false;
        for (var page = 1; page <= MaxListPages; page++)
        {
            var query = Query(null, ListFields, "id", PageSize, page);
            query["user"] = userId;
            var text = await SendAsync(HttpMethod.Post, "ulist", query.ToJsonString(), authorization, ct).ConfigureAwait(false);
            var parsed = Parse<VndbWire.Page<VndbWire.ListEntry>>(text);
            entries.AddRange(parsed.Results.Select(VndbWire.ToCore));
            if (!parsed.More)
            {
                break;
            }

            truncated = page == MaxListPages;
        }

        return new VndbResults<VndbListEntry>(entries, truncated);
    }

    /// <summary>
    /// <c>PATCH /ulist/&lt;id&gt;</c> (seção "PATCH /ulist/"): exige <c>listwrite</c>; aceita <c>vote</c> (10 a 100), <c>notes</c>, <c>started</c>, <c>finished</c>,
    /// <c>labels_set</c> e <c>labels_unset</c> (o <c>labels</c> sobrescreve todos e por isso não é usado aqui). Responde 204.
    /// </summary>
    public async Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default)
    {
        VndbRules.RequireId(visualNovelId, 'v');
        VndbRules.RequireValid(update);
        var body = BuildUpdateBody(update);
        await SendAsync(new HttpMethod("PATCH"), "ulist/" + visualNovelId, body.ToJsonString(), RequireToken(token), ct).ConfigureAwait(false);
    }

    private static JsonObject BuildUpdateBody(VndbListUpdate update)
    {
        var body = new JsonObject();
        AddChange(body, "vote", update.Vote, vote => JsonValue.Create(vote));
        AddChange(body, "notes", update.Notes, notes => JsonValue.Create(notes));
        AddChange(body, "started", update.Started, date => JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        AddChange(body, "finished", update.Finished, date => JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        if (update.LabelsSet.Count > 0)
        {
            body["labels_set"] = new JsonArray([.. update.LabelsSet.Select(id => (JsonNode?)JsonValue.Create(id))]);
        }

        if (update.LabelsUnset.Count > 0)
        {
            body["labels_unset"] = new JsonArray([.. update.LabelsUnset.Select(id => (JsonNode?)JsonValue.Create(id))]);
        }

        return body;
    }

    private static void AddChange<T>(JsonObject body, string name, VndbChange<T> change, Func<T, JsonNode?> toJson)
    {
        switch (change.Kind)
        {
            case VndbChangeKind.Set:
                body[name] = toJson(change.Value!);
                break;
            case VndbChangeKind.Clear:
                body[name] = null;
                break;
        }
    }

    private static JsonObject Query(JsonNode? filters, string fields, string? sort, int results, int page)
    {
        var query = new JsonObject();
        if (filters is not null)
        {
            query["filters"] = filters;
        }

        query["fields"] = fields;
        if (sort is not null)
        {
            query["sort"] = sort;
        }

        query["results"] = results;
        query["page"] = page;
        return query;
    }

    private async Task<(List<T> Items, bool Truncated)> QueryAllAsync<T>(string path, Func<JsonNode> filters, string fields, string? sort, int maxPages, CancellationToken ct)
    {
        var all = new List<T>();
        var truncated = false;
        for (var page = 1; page <= maxPages; page++)
        {
            var parsed = await QueryPageAsync<T>(path, Query(filters(), fields, sort, PageSize, page), ct).ConfigureAwait(false);
            all.AddRange(parsed.Results);
            if (!parsed.More)
            {
                break;
            }

            truncated = page == maxPages;
        }

        return (all, truncated);
    }

    private async Task<VndbWire.Page<T>> QueryPageAsync<T>(string path, JsonObject query, CancellationToken ct)
    {
        var body = query.ToJsonString();
        var key = VndbCache.KeyFor(path, body);
        if (_cache is not null && _cache.TryGet(key, out var cached) && TryParse<VndbWire.Page<T>>(cached) is { } fromCache)
        {
            return fromCache;
        }

        var text = await SendAsync(HttpMethod.Post, path, body, null, ct).ConfigureAwait(false);
        var page = Parse<VndbWire.Page<T>>(text);
        _cache?.Set(key, text);
        return page;
    }

    private async Task<string> SendAsync(HttpMethod method, string path, string? body, string? token, CancellationToken ct)
    {
        for (var throttled = 0; ; throttled++)
        {
            await _queue.WaitTurnAsync(ct).ConfigureAwait(false);
            var reply = await ExchangeAsync(method, path, body, token, ct).ConfigureAwait(false);
            if (reply.Status is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
            {
                return reply.Text;
            }

            var isThrottle = IsThrottle(reply);
            if (!isThrottle || throttled >= ThrottleWaits.Length)
            {
                throw ErrorFor(reply, isThrottle);
            }

            // O Retry-After, quando vem, manda; sem ele a espera cresce a cada tentativa. Nunca passa da janela da fila.
            var wait = reply.RetryAfter is { } asked && asked > TimeSpan.Zero ? asked : ThrottleWaits[throttled];
            _queue.BlockFor(wait < VndbRequestQueue.DefaultWindow ? wait : VndbRequestQueue.DefaultWindow);
        }
    }

    /// <summary>
    /// Manda o pedido e lê a resposta inteira sob o mesmo prazo e o mesmo tratamento de erro: uma queda no meio do corpo ou um corpo que trava
    /// não escapam como exceção de rede crua nem seguram o cliente para sempre.
    /// </summary>
    private async Task<Reply> ExchangeAsync(HttpMethod method, string path, string? body, string? token, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(_timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.UserAgent.ParseAdd("VNForge");
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return new Reply(response.StatusCode, text, RetryAfterOf(response.Headers.RetryAfter));
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new VndbException("O VNDB demorou demais para responder. Tente de novo.", null, e);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw new VndbException("Não foi possível falar com o VNDB, ou a conexão caiu no meio da resposta. Confira a conexão e tente de novo.", null, e);
        }
    }

    private TimeSpan? RetryAfterOf(RetryConditionHeaderValue? header)
    {
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - _time.GetUtcNow() : null;
    }

    /// <summary>
    /// 429 é limite de uso. 503 só conta como limite quando a resposta diz: o nginx na frente do VNDB devolve a página &quot;Service Temporarily Unavailable&quot;
    /// (ou manda <c>Retry-After</c>) ao limitar; um 503 sem isso é o servidor fora do ar.
    /// </summary>
    private static bool IsThrottle(Reply reply) =>
        reply.Status == HttpStatusCode.TooManyRequests
        || (reply.Status == HttpStatusCode.ServiceUnavailable
            && (reply.RetryAfter is not null || reply.Text.Contains("Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase)));

    private static VndbException ErrorFor(Reply reply, bool throttled)
    {
        var code = (int)reply.Status;
        var message = throttled
            ? "O VNDB limitou os pedidos. Tente de novo em alguns minutos."
            : reply.Status switch
            {
                HttpStatusCode.BadRequest => "O VNDB recusou o pedido: " + Shorten(reply.Text),
                HttpStatusCode.Unauthorized => "O VNDB não aceitou o token. Gere outro em vndb.org/u/tokens.",
                >= HttpStatusCode.InternalServerError => $"O VNDB está fora do ar no momento (HTTP {code}). Tente de novo mais tarde.",
                _ => $"O VNDB respondeu com erro HTTP {code}.",
            };
        return new VndbException(message, code);
    }

    private static string Shorten(string text)
    {
        var single = text.Trim();
        return single.Length <= 200 ? single : single[..200] + "...";
    }

    private static T Parse<T>(string text)
        where T : class =>
        TryParse<T>(text) ?? throw new VndbException("O VNDB devolveu uma resposta que não foi possível ler.");

    private static T? TryParse<T>(string text)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, VndbWire.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RequireToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var trimmed = token.Trim();
        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new VndbException("O token do VNDB tem espaço ou caractere de controle; copie-o de novo de vndb.org/u/tokens.");
        }

        return trimmed;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private sealed record Reply(HttpStatusCode Status, string Text, TimeSpan? RetryAfter);
}
