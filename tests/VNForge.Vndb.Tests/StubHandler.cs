using System.Net;
using System.Text;

namespace VNForge.Vndb.Tests;

/// <summary>Um pedido que o cliente mandou, lido por inteiro.</summary>
internal sealed record SentRequest(HttpMethod Method, Uri Uri, string Body, string? Authorization, string? UserAgent);

/// <summary>
/// O transporte HTTP dos testes: responde com o que o teste manda e guarda o que recebeu. Nenhum teste do cliente abre conexão de rede, porque o único
/// <see cref="HttpClient"/> usado é o que tem este handler.
/// </summary>
internal sealed class StubHandler(Func<SentRequest, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<SentRequest> _requests = [];
    private readonly Lock _gate = new();

    public StubHandler(string json)
        : this((_, _) => Json(json))
    {
    }

    public IReadOnlyList<SentRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(HttpStatusCode status, string text) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var sent = new SentRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.ToString(),
            request.Headers.UserAgent.ToString());
        int number;
        lock (_gate)
        {
            _requests.Add(sent);
            number = _requests.Count;
        }

        return respond(sent, number);
    }
}
