using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Output;

/// <summary>
/// Formato comum do <c>--json</c> da CLI, em uma linha por documento:
/// <c>{"ok":true,"exitCode":0,"data":{...}}</c> para resultado e
/// <c>{"ok":false,"exitCode":1,"error":{"kind":"expected","message":"...","logDirectory":"..."}}</c> para erro.
/// <c>kind</c> é <c>expected</c>, <c>unexpected</c> ou <c>cancelled</c>; <c>message</c> está em português
/// e nunca carrega stack trace.
/// </summary>
public static class JsonEnvelope
{
    public const string ExpectedKind = "expected";

    public const string UnexpectedKind = "unexpected";

    public const string CancelledKind = "cancelled";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Result(object? data, int exitCode = 0) =>
        JsonSerializer.Serialize(new ResultDocument(true, exitCode, data), Options);

    public static string Error(string kind, string message, int exitCode, string? logDirectory = null, string? detail = null) =>
        JsonSerializer.Serialize(
            new ErrorDocument(false, exitCode, new ErrorInfo(kind, message, logDirectory, detail)),
            Options);

    private sealed record ResultDocument(bool Ok, int ExitCode, object? Data);

    private sealed record ErrorDocument(bool Ok, int ExitCode, ErrorInfo Error);

    private sealed record ErrorInfo(string Kind, string Message, string? LogDirectory, string? Detail);
}
