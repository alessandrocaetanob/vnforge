using System.Text.Json;

namespace VNForge.Core.Projects;

/// <summary>Um projeto aberto recentemente e quando foi aberto.</summary>
public sealed record RecentProject(string Path, DateTimeOffset OpenedAt);

/// <summary>Os recentes lidos de <c>recent.json</c>; <see cref="Problem"/> diz por que a lista veio vazia quando o arquivo existe e não foi lido.</summary>
public sealed record RecentProjectsList(IReadOnlyList<RecentProject> Projects, string? Problem = null);

/// <summary>
/// Lista de projetos recentes em <c>recent.json</c>, na pasta de configuração do usuário (<c>%APPDATA%/VNForge</c>), fora de qualquer projeto.
/// É uma conveniência: um arquivo ilegível vale como lista vazia e o próximo <see cref="Add"/> o substitui.
/// </summary>
public sealed class RecentProjectsFile(string directory, int capacity = RecentProjectsFile.DefaultCapacity)
{
    public const int DefaultCapacity = 10;

    private const string FileName = "recent.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    /// <summary>Pasta de configuração padrão: <c>%APPDATA%/VNForge</c>, ou a pasta temporária se o sistema não informar uma absoluta.</summary>
    public static string DefaultDirectory()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDirectory = !string.IsNullOrWhiteSpace(roaming) && System.IO.Path.IsPathRooted(roaming)
            ? roaming
            : System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(baseDirectory, "VNForge");
    }

    /// <summary>Os projetos recentes, o mais recente primeiro. Arquivo ilegível vale como lista vazia; <see cref="Read"/> diz por quê.</summary>
    public IReadOnlyList<RecentProject> Load() => Read().Projects;

    /// <summary>Como <see cref="Load"/>, e com o <see cref="RecentProjectsList.Problem"/> (com o caminho do arquivo) quando <c>recent.json</c> existe e não pôde ser lido.</summary>
    public RecentProjectsList Read()
    {
        if (!File.Exists(Path))
        {
            return new RecentProjectsList([]);
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(Path));
            if (items is null)
            {
                return new RecentProjectsList([], $"O arquivo '{Path}' não tem uma lista de projetos.");
            }

            return new RecentProjectsList(items
                .Where(r => !string.IsNullOrWhiteSpace(r?.Path))
                .OrderByDescending(r => r.OpenedAt)
                .ToList());
        }
        catch (JsonException)
        {
            return new RecentProjectsList([], $"O arquivo '{Path}' não está no formato esperado.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new RecentProjectsList([], $"O arquivo '{Path}' não pôde ser lido: {e.Message}");
        }
    }

    /// <summary>Registra a abertura de <paramref name="path"/>; se já estava na lista, sobe para o topo. Mantém só os <c>capacity</c> mais recentes.</summary>
    public void Add(string path, DateTimeOffset openedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var key = Normalize(path);
        var items = Load()
            .Where(r => !string.Equals(Normalize(r.Path), key, StringComparison.OrdinalIgnoreCase))
            .Prepend(new RecentProject(key, openedAt))
            .Take(capacity)
            .ToList();
        Save(items);
    }

    public void Remove(string path)
    {
        var key = Normalize(path);
        Save(Load().Where(r => !string.Equals(Normalize(r.Path), key, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private static string Normalize(string path) => path.TrimEnd('\\', '/');

    private void Save(List<RecentProject> items)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, Json));
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
