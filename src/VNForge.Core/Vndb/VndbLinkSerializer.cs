using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Vndb;

/// <summary>Lê e grava <c>vndb.json</c>: <c>{ "version": 1, "link": { ... } }</c> em camelCase, com o japonês sem escape e quebra de linha LF.</summary>
public static class VndbLinkSerializer
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(VndbLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return JsonSerializer.Serialize(new Envelope(CurrentVersion, link), Options) + "\n";
    }

    /// <exception cref="VndbLinkException">JSON malformado, campo desconhecido ou ausente, ou versão desconhecida.</exception>
    public static VndbLink Parse(string json, string filePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new VndbLinkException($"O arquivo '{filePath}' não é um vndb.json válido: {ex.Message}", ex);
        }

        if (envelope?.Link is null)
        {
            throw new VndbLinkException($"O arquivo '{filePath}' não é um vndb.json válido: falta o jogo.");
        }

        return envelope.Version == CurrentVersion
            ? envelope.Link
            : throw new VndbLinkException($"O arquivo '{filePath}' é da versão {envelope.Version} do vndb.json, e esta versão do VNForge lê só a {CurrentVersion}.");
    }

    private sealed record Envelope([property: JsonRequired] int Version, [property: JsonRequired] VndbLink? Link);
}
