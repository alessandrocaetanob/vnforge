using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Patching;

/// <summary>
/// Os metadados do JSON do patch e do manifesto da instalação, gerados na compilação (source generation). Sem reflexão, o instalador publicado
/// passa pelo trimming: o que o trimmer não enxerga não é cortado nem quebra em tempo de execução.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PatchManifest))]
[JsonSerializable(typeof(PatchInstaller.InstallManifest))]
internal sealed partial class PatchJsonContext : JsonSerializerContext
{
    /// <summary>A instância única; o codificador deixa acentos e símbolos como estão (o manifesto é lido por gente, e o leia-me tem acentos).</summary>
    internal static PatchJsonContext Instance { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    });
}
