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
/// </summary>
public sealed class VndbClient : IVndbCatalog, IVndbUserList
{
    public static readonly Uri DefaultBaseUri = new("https://api.vndb.org/kana/");

    private const string VisualNovelFields =
        "title,alttitle,titles.lang,titles.title,titles.latin,titles.official,titles.main,aliases,olang,devstatus,released,languages,platforms,"
        + "image.url,image.thumbnail,image.dims,image.sexual,image.violence,length,length_minutes,length_votes,description,"
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
    private const int MaxThrottleRetries = 1;
    private static readonly TimeSpan DefaultThrottleWait = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly VndbRequestQueue _queue;
    private readonly VndbCache? _cache;
    private readonly Uri _baseUri;

    public VndbClient(HttpClient http, VndbRequestQueue queue, VndbCache? cache = null, Uri? baseUri = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(queue);
        _http = http;
        _queue = queue;
        _cache = cache;
        _baseUri = EnsureTrailingSlash(baseUri ?? DefaultBaseUri);
    }

    /// <summary>Um cliente pronto para o endpoint de produção, com a fila de 200 pedidos a cada 5 minutos e o cache na pasta dada (sem pasta, não há cache em disco).</summary>
    public static VndbClient Create(string? cacheDirectory = null, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        return new VndbClient(
            http,
            new VndbRequestQueue(time),
            cacheDirectory is null ? null : new VndbCache(cacheDirectory, time));
    }

    public async Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var query = Query(new JsonArray("search", "=", title.Trim()), VisualNovelFields, "searchrank", Math.Clamp(limit, 1, PageSize), 1);
        var page = await QueryPageAsync<VndbWire.VisualNovel>("vn", query, ct).ConfigureAwait(false);
        return [.. page.Results.Select(VndbWire.ToCore)];
    }

    public async Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default)
    {
        RequireId(id, 'v');
        var query = Query(new JsonArray("id", "=", id), VisualNovelFields, null, 1, 1);
        var page = await QueryPageAsync<VndbWire.VisualNovel>("vn", query, ct).ConfigureAwait(false);
        return page.Results.Count == 0 ? null : VndbWire.ToCore(page.Results[0]);
    }

    public async Task<IReadOnlyList<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default)
    {
        RequireId(visualNovelId, 'v');
        var results = await QueryAllAsync<VndbWire.Character>(
            "character",
            () => new JsonArray("vn", "=", new JsonArray("id", "=", visualNovelId)),
            CharacterFields,
            null,
            MaxCharacterPages,
            ct).ConfigureAwait(false);
        return [.. results.Select(VndbWire.ToCore)];
    }

    public async Task<IReadOnlyList<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var text = title.Trim();
        var results = await QueryAllAsync<VndbWire.Character>(
            "character",
            () => new JsonArray("vn", "=", new JsonArray("search", "=", text)),
            CharacterFields,
            null,
            MaxSearchCharacterPages,
            ct).ConfigureAwait(false);
        return [.. results.Select(VndbWire.ToCore)];
    }

    public async Task<IReadOnlyList<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default)
    {
        RequireId(visualNovelId, 'v');
        var results = await QueryAllAsync<VndbWire.Release>(
            "release",
            () => new JsonArray("vn", "=", new JsonArray("id", "=", visualNovelId)),
            ReleaseFields,
            "released",
            MaxReleasePages,
            ct).ConfigureAwait(false);
        return [.. results.Select(VndbWire.ToCore)];
    }

    public async Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default)
    {
        var text = await SendAsync(HttpMethod.Get, "authinfo", null, RequireToken(token), ct).ConfigureAwait(false);
        var info = Parse<VndbWire.AuthInfo>(text);
        return new VndbAuthInfo(info.Id, info.Username, [.. info.Permissions ?? []]);
    }

    public async Task<IReadOnlyList<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default)
    {
        RequireId(userId, 'u');
        var authorization = token is null ? null : RequireToken(token);
        var entries = new List<VndbListEntry>();
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
        }

        return entries;
    }

    public async Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        RequireId(visualNovelId, 'v');
        var body = BuildUpdateBody(update);
        await SendAsync(new HttpMethod("PATCH"), "ulist/" + visualNovelId, body.ToJsonString(), RequireToken(token), ct).ConfigureAwait(false);
    }

    private static JsonObject BuildUpdateBody(VndbListUpdate update)
    {
        var body = new JsonObject();
        AddChange(body, "vote", update.Vote, vote =>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(vote, 10, nameof(update));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(vote, 100, nameof(update));
            return JsonValue.Create(vote);
        });
        AddChange(body, "notes", update.Notes, notes => JsonValue.Create(notes));
        AddChange(body, "started", update.Started, date => JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        AddChange(body, "finished", update.Finished, date => JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        // Os rótulos 0 ("sem rótulo") e 7 ("votou") são virtuais: o VNDB não os aceita, o de voto sai sozinho com o campo vote.
        if (update.LabelsSet.Any(id => id is 0 or 7))
        {
            throw new ArgumentOutOfRangeException(nameof(update), "Os rótulos 0 e 7 são virtuais e não podem ser gravados.");
        }

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

    private async Task<List<T>> QueryAllAsync<T>(string path, Func<JsonNode> filters, string fields, string? sort, int maxPages, CancellationToken ct)
    {
        var all = new List<T>();
        for (var page = 1; page <= maxPages; page++)
        {
            var parsed = await QueryPageAsync<T>(path, Query(filters(), fields, sort, PageSize, page), ct).ConfigureAwait(false);
            all.AddRange(parsed.Results);
            if (!parsed.More)
            {
                break;
            }
        }

        return all;
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
        for (var attempt = 0; ; attempt++)
        {
            await _queue.WaitTurnAsync(ct).ConfigureAwait(false);
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

            using var response = await SendOnceAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return text;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxThrottleRetries)
            {
                _queue.BlockFor(response.Headers.RetryAfter?.Delta ?? DefaultThrottleWait);
                continue;
            }

            throw ErrorFor(response.StatusCode, text);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new VndbException("Não foi possível falar com o VNDB. Confira a conexão e tente de novo.", null, e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new VndbException("O VNDB demorou demais para responder. Tente de novo.", null, e);
        }
    }

    private static VndbException ErrorFor(HttpStatusCode status, string body)
    {
        var code = (int)status;
        var message = status switch
        {
            HttpStatusCode.BadRequest => "O VNDB recusou o pedido: " + Shorten(body),
            HttpStatusCode.Unauthorized => "O VNDB não aceitou o token. Gere outro em vndb.org/u/tokens.",
            HttpStatusCode.TooManyRequests => "O VNDB limitou os pedidos. Tente de novo em alguns minutos.",
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

    private static void RequireId(string id, char prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length < 2 || id[0] != prefix || id.AsSpan(1).IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new ArgumentException($"Id do VNDB inválido: '{id}'. O esperado é como '{prefix}17'.", nameof(id));
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
