using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>
/// O catálogo do VNDB em memória, para os testes e para o app sem rede. Responde só com o que foi posto nele, sem chamar a rede nem esperar fila.
/// A busca por título compara sem diferenciar maiúsculas e olha título, título alternativo, apelidos e os títulos por idioma. A lista faz de conta que há uma conta só,
/// a do token <see cref="ValidToken"/>.
/// </summary>
public sealed class FakeVndbCatalog : IVndbCatalog, IVndbUserList
{
    public const string ValidToken = "fake-token";

    private readonly List<VndbVisualNovel> _visualNovels;
    private readonly List<VndbCharacter> _characters;
    private readonly List<VndbRelease> _releases;
    private readonly Dictionary<string, VndbListEntry> _list = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];
    private readonly Lock _gate = new();
    private readonly string _userId;

    public FakeVndbCatalog(
        IEnumerable<VndbVisualNovel>? visualNovels = null,
        IEnumerable<VndbCharacter>? characters = null,
        IEnumerable<VndbRelease>? releases = null,
        IEnumerable<VndbListEntry>? list = null,
        string userId = "u1")
    {
        _visualNovels = [.. visualNovels ?? []];
        _characters = [.. characters ?? []];
        _releases = [.. releases ?? []];
        _userId = userId;
        foreach (var entry in list ?? [])
        {
            _list[entry.VisualNovelId] = entry;
        }
    }

    /// <summary>Os métodos chamados, na ordem, no formato <c>Método(argumento)</c>.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default)
    {
        Record(nameof(SearchVisualNovelsAsync), title);
        return Task.FromResult<IReadOnlyList<VndbVisualNovel>>([.. Matching(title).Take(Math.Max(limit, 0))]);
    }

    public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default)
    {
        Record(nameof(GetVisualNovelAsync), id);
        return Task.FromResult(_visualNovels.FirstOrDefault(v => v.Id == id));
    }

    public Task<IReadOnlyList<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default)
    {
        Record(nameof(GetCharactersAsync), visualNovelId);
        return Task.FromResult<IReadOnlyList<VndbCharacter>>([.. CharactersOf([visualNovelId])]);
    }

    public Task<IReadOnlyList<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default)
    {
        Record(nameof(SearchCharactersByTitleAsync), title);
        return Task.FromResult<IReadOnlyList<VndbCharacter>>([.. CharactersOf(Matching(title).Select(v => v.Id).ToHashSet(StringComparer.Ordinal))]);
    }

    public Task<IReadOnlyList<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default)
    {
        Record(nameof(GetReleasesAsync), visualNovelId);
        return Task.FromResult<IReadOnlyList<VndbRelease>>([.. _releases.Where(r => r.VisualNovels.Any(v => v.VisualNovelId == visualNovelId))]);
    }

    public Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default)
    {
        Record(nameof(GetAuthInfoAsync), "***");
        RequireValid(token);
        return Task.FromResult(new VndbAuthInfo(_userId, "fake", ["listread", "listwrite"]));
    }

    public Task<IReadOnlyList<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default)
    {
        Record(nameof(GetListAsync), userId);
        if (token is not null)
        {
            RequireValid(token);
        }

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<VndbListEntry>>(userId == _userId ? [.. _list.Values.OrderBy(e => e.VisualNovelId, StringComparer.Ordinal)] : []);
        }
    }

    public Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        Record(nameof(UpdateEntryAsync), visualNovelId);
        RequireValid(token);
        lock (_gate)
        {
            var now = DateTimeOffset.UnixEpoch;
            _list.TryGetValue(visualNovelId, out var current);
            current ??= new VndbListEntry(visualNovelId, _visualNovels.FirstOrDefault(v => v.Id == visualNovelId)?.Title, now, now, null, null, null, null, null, []);

            var labels = current.Labels
                .Where(l => !update.LabelsUnset.Contains(l.Id))
                .Concat(update.LabelsSet.Where(id => current.Labels.All(l => l.Id != id)).Select(id => new VndbListLabel(id, "#" + id)))
                .ToList();
            _list[visualNovelId] = current with
            {
                Vote = Apply(current.Vote, update.Vote),
                Notes = Apply(current.Notes, update.Notes),
                Started = Apply(current.Started, update.Started),
                Finished = Apply(current.Finished, update.Finished),
                Labels = labels,
            };
        }

        return Task.CompletedTask;
    }

    private static T? Apply<T>(T? current, VndbChange<T> change)
        where T : struct =>
        change.Kind switch
        {
            VndbChangeKind.Set => change.Value,
            VndbChangeKind.Clear => null,
            _ => current,
        };

    private static string? Apply(string? current, VndbChange<string> change) =>
        change.Kind switch
        {
            VndbChangeKind.Set => change.Value,
            VndbChangeKind.Clear => null,
            _ => current,
        };

    private static void RequireValid(string token)
    {
        if (token != ValidToken)
        {
            throw new VndbException("O VNDB não aceitou o token. Gere outro em vndb.org/u/tokens.", 401);
        }
    }

    private IEnumerable<VndbVisualNovel> Matching(string title) =>
        _visualNovels.Where(v => Names(v).Any(name => name.Contains(title.Trim(), StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> Names(VndbVisualNovel v)
    {
        yield return v.Title;
        if (v.AltTitle is not null)
        {
            yield return v.AltTitle;
        }

        foreach (var alias in v.Aliases)
        {
            yield return alias;
        }

        foreach (var title in v.Titles)
        {
            yield return title.Title;
            if (title.Latin is not null)
            {
                yield return title.Latin;
            }
        }
    }

    private IEnumerable<VndbCharacter> CharactersOf(ICollection<string> visualNovelIds) =>
        _characters.Where(c => c.Appearances.Any(a => visualNovelIds.Contains(a.VisualNovelId)));

    private void Record(string method, string argument)
    {
        lock (_gate)
        {
            _calls.Add($"{method}({argument})");
        }
    }
}
