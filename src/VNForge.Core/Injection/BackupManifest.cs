using System.Text.Encodings.Web;
using System.Text.Json;

namespace VNForge.Core.Injection;

/// <summary>
/// O que uma injeção fez, gravado em <c>backup/&lt;data&gt;/vnforge-backup.json</c> antes de qualquer escrita no jogo.
/// É por ele que <c>restore</c> sabe o que devolver, mesmo quando a injeção morreu no meio.
/// </summary>
public sealed record BackupManifest(int Version, DateTimeOffset CreatedAt, string GameDirectory, IReadOnlyList<BackupEntry> Files)
{
    public const string FileName = "vnforge-backup.json";

    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal string Serialize() => JsonSerializer.Serialize(this, Options);

    internal static BackupManifest Parse(string json, string path)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(json, Options);
            if (manifest is null || manifest.Version != CurrentVersion || manifest.Files is null || manifest.Files.Any(IsInvalid))
            {
                throw new InjectionException($"O manifesto do backup está fora do formato esperado: {path}");
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            throw new InjectionException($"O manifesto do backup não pôde ser lido: {path}", ex);
        }
    }

    private static bool IsInvalid(BackupEntry entry) =>
        entry is null
        || string.IsNullOrWhiteSpace(entry.Path)
        || string.IsNullOrWhiteSpace(entry.InjectedSha256)
        || (entry.Existed && string.IsNullOrWhiteSpace(entry.OriginalSha256));
}

/// <param name="Path">Caminho relativo à pasta do jogo, com <c>/</c>.</param>
/// <param name="Existed">O jogo tinha o arquivo antes da injeção. Se não tinha, <c>restore</c> apaga o que a injeção criou.</param>
/// <param name="OriginalSha256">SHA-256 do original; nulo quando <paramref name="Existed"/> é falso.</param>
/// <param name="InjectedSha256">SHA-256 do arquivo do build gravado no jogo.</param>
public sealed record BackupEntry(string Path, bool Existed, string? OriginalSha256, string InjectedSha256);
