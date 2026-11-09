using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// As tags próprias em <c>library-tags.json</c>, ao lado do <c>library.json</c>, na pasta de configuração do usuário. Fica num arquivo à parte para que reescrever a
/// biblioteca (varredura, casamento) nunca toque nas tags, e as tags de um jogo que saiu da lista voltam se ele for achado de novo (o id vem da pasta).
/// Como a biblioteca, um arquivo ilegível não vira lista vazia: <see cref="Load"/> recusa em vez de deixar o próximo <see cref="Save"/> sobrescrevê-lo.
/// </summary>
public sealed class UserTagsFile(string directory)
{
    public const string FileName = "library-tags.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    /// <summary>As tags do arquivo; vazias quando o arquivo não existe.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato; a mensagem cita o arquivo.</exception>
    public UserTags Load()
    {
        if (!File.Exists(Path))
        {
            return new UserTags();
        }

        try
        {
            return Parse(File.ReadAllText(Path), Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"O arquivo '{Path}' não pôde ser lido: {e.Message}", e);
        }
    }

    /// <summary>Grava as tags sem deixar o arquivo pela metade: escreve num temporário ao lado e troca.</summary>
    public void Save(UserTags tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, Serialize(tags));
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>O JSON das tags, sempre na mesma ordem (tags e jogos por nome e por id), para o arquivo mudar só quando as tags mudam.</summary>
    public static string Serialize(UserTags tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var games = new SortedDictionary<string, List<string>?>(StringComparer.Ordinal);
        foreach (var id in tags.GameIds)
        {
            games[id] = [.. tags.TagsOf(id)];
        }

        var filter = tags.SavedFilter;
        var filterEntry = filter.IsEmpty ? null : new FilterEntry([.. filter.Include], filter.Any, [.. filter.Exclude]);
        return JsonSerializer.Serialize(new Envelope(CurrentVersion, [.. tags.Tags.Select(t => new TagEntry(t.Name, t.Color))], games, filterEntry), Json);
    }

    /// <exception cref="LibraryException">JSON malformado, versão desconhecida, tag inválida ou repetida, jogo com tag que não existe, ou filtro inválido.</exception>
    public static UserTags Parse(string json, string filePath = "(memória)")
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, Json);
        }
        catch (JsonException e)
        {
            throw new LibraryException($"O arquivo '{filePath}' não está no formato esperado: {e.Message}", e);
        }

        if (envelope is null)
        {
            throw new LibraryException($"O arquivo '{filePath}' está vazio.");
        }

        if (envelope.Version != CurrentVersion)
        {
            throw new LibraryException($"O arquivo '{filePath}' é da versão {envelope.Version} das tags da biblioteca; esta versão do VNForge lê a {CurrentVersion}.");
        }

        try
        {
            return Build(envelope);
        }
        catch (LibraryException e)
        {
            throw new LibraryException($"O arquivo '{filePath}': {e.Message}", e);
        }
    }

    private static UserTags Build(Envelope envelope)
    {
        var tags = new UserTags();
        foreach (var entry in envelope.Tags ?? [])
        {
            if (entry?.Name is null)
            {
                throw new LibraryException("há uma tag sem nome.");
            }

            var name = UserTagName.Normalize(entry.Name);
            if (tags.TryGet(name, out _))
            {
                throw new LibraryException($"a tag '{name}' aparece mais de uma vez.");
            }

            tags.Define(name, entry.Color);
        }

        foreach (var (gameId, names) in envelope.Games ?? [])
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                throw new LibraryException("há tags de um jogo sem id.");
            }

            foreach (var name in names ?? [])
            {
                if (!tags.TryGet(name, out _))
                {
                    throw new LibraryException($"o jogo {gameId} usa a tag '{UserTagName.Collapse(name)}', que não está na lista de tags.");
                }

                tags.AssignExisting(gameId, name);
            }
        }

        if (envelope.Filter is { } filter)
        {
            tags.SavedFilter = LibraryTagFilter.Create(filter.Include, filter.Any, filter.Exclude);
        }

        return tags;
    }

    private sealed record Envelope(int Version, List<TagEntry?>? Tags, SortedDictionary<string, List<string>?>? Games, FilterEntry? Filter);

    private sealed record TagEntry(string? Name, string? Color);

    private sealed record FilterEntry(List<string>? Include, bool Any, List<string>? Exclude);
}
