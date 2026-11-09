using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Memory;

/// <summary>
/// Arquivo JSON do usuário, na pasta de configuração, com o envelope <c>{ "version": 1, "entries": [...] }</c> e uma entrada por linha.
/// Diferente de <c>recent.json</c>, um arquivo ilegível não vira lista vazia: a gravação seguinte o apagaria, e a memória vale meses de trabalho.
/// </summary>
internal sealed class UserEntriesFile<T>(string path)
    where T : class
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
    };

    public string Path { get; } = path;

    public bool Exists => File.Exists(Path);

    /// <summary>Marca de mudança do arquivo (data e tamanho), para quem guarda uma cópia em memória saber se ela envelheceu.</summary>
    public (DateTime Stamp, long Length) Stamp()
    {
        var info = new FileInfo(Path);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
    }

    /// <exception cref="MemoryFileException">O arquivo existe e não pôde ser lido ou não segue o formato.</exception>
    public List<T> Load()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        try
        {
            var json = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Projects.ProjectFileIo.ReadAllBytes(Path));
            var envelope = JsonSerializer.Deserialize<Envelope>(json, Options) ?? throw new JsonException("o arquivo está vazio.");
            if (envelope.Version != CurrentVersion)
            {
                throw new MemoryFileException(Path, $"a versão {envelope.Version} não é conhecida por esta versão do VNForge.");
            }

            return envelope.Entries?.Where(e => e is not null).ToList() ?? throw new JsonException("falta a lista 'entries'.");
        }
        catch (MemoryFileException)
        {
            throw;
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException)
        {
            throw new MemoryFileException(Path, "o conteúdo não está no formato esperado.", e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new MemoryFileException(Path, e.Message, e);
        }
    }

    /// <exception cref="MemoryFileException">A gravação falhou; o arquivo anterior continua como estava.</exception>
    public void Save(IReadOnlyList<T> entries)
    {
        var sb = new StringBuilder("{\n  \"version\": ").Append(CurrentVersion).Append(",\n  \"entries\": [");
        for (var i = 0; i < entries.Count; i++)
        {
            sb.Append(i == 0 ? "\n    " : ",\n    ").Append(JsonSerializer.Serialize(entries[i], Options));
        }

        sb.Append(entries.Count == 0 ? "]\n}\n" : "\n  ]\n}\n");

        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(temporary, sb.ToString(), new UTF8Encoding(false));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new MemoryFileException(Path, e.Message, e);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // O temporário solto não atrapalha nada; a próxima gravação usa outro nome.
            }
        }
    }

    private sealed record Envelope(int Version, List<T>? Entries);
}
