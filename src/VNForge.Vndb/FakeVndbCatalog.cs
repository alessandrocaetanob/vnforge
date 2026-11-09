using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>
/// O catálogo do VNDB em memória, para os testes e para o app sem rede. Responde só com o que foi posto nele, sem chamar a rede nem esperar fila.
/// Fica em <c>src/VNForge.Vndb</c>, e não num projeto de testes, pelo mesmo motivo do <c>FakeTranslationProvider</c>: o app e a CLI o usam sem rede e os testes de
/// todos os projetos que dependem do VNDB (Biblioteca, tela, CLI) o compartilham.
/// A busca por título compara sem diferenciar maiúsculas e olha título, título alternativo, apelidos e os títulos por idioma. Valida a entrada como o cliente
/// real (<see cref="VndbRules"/>) e imita o servidor na lista: votar põe o rótulo 7 (&quot;Voted&quot;) e a data do voto, apagar o voto tira os dois, e a lista sai
/// ordenada pelo número do id. Faz de conta que há uma conta só, a do token <see cref="ValidToken"/>.
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
    private readonly TimeProvider _time;

    public FakeVndbCatalog(
        IEnumerable<VndbVisualNovel>? visualNovels = null,
        IEnumerable<VndbCharacter>? characters = null,
        IEnumerable<VndbRelease>? releases = null,
        IEnumerable<VndbListEntry>? list = null,
        string userId = "u1",
        TimeProvider? time = null)
    {
        _visualNovels = [.. visualNovels ?? []];
        _characters = [.. characters ?? []];
        _releases = [.. releases ?? []];
        _userId = userId;
        _time = time ?? TimeProvider.System;
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
        var text = VndbRules.RequireTitle(title);
        Record(nameof(SearchVisualNovelsAsync), title);
        return Task.FromResult<IReadOnlyList<VndbVisualNovel>>([.. Matching(text).Take(Math.Clamp(limit, 1, 100))]);
    }

    public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default)
    {
        VndbRules.RequireId(id, 'v');
        Record(nameof(GetVisualNovelAsync), id);
        return Task.FromResult(_visualNovels.FirstOrDefault(v => v.Id == id));
    }

    public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default)
    {
        VndbRules.RequireId(visualNovelId, 'v');
        Record(nameof(GetCharactersAsync), visualNovelId);
        return Task.FromResult(new VndbResults<VndbCharacter>([.. CharactersOf([visualNovelId])], false));
    }

    public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default)
    {
        var text = VndbRules.RequireTitle(title);
        Record(nameof(SearchCharactersByTitleAsync), title);
        return Task.FromResult(new VndbResults<VndbCharacter>([.. CharactersOf(Matching(text).Select(v => v.Id).ToHashSet(StringComparer.Ordinal))], false));
    }

    public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default)
    {
        VndbRules.RequireId(visualNovelId, 'v');
        Record(nameof(GetReleasesAsync), visualNovelId);
        return Task.FromResult(new VndbResults<VndbRelease>([.. _releases.Where(r => r.VisualNovels.Any(v => v.VisualNovelId == visualNovelId))], false));
    }

    public Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default)
    {
        Record(nameof(GetAuthInfoAsync), "***");
        RequireValid(token);
        return Task.FromResult(new VndbAuthInfo(_userId, "fake", ["listread", "listwrite"]));
    }

    public Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default)
    {
        VndbRules.RequireId(userId, 'u');
        Record(nameof(GetListAsync), userId);
        if (token is not null)
        {
            RequireValid(token);
        }

        lock (_gate)
        {
            IReadOnlyList<VndbListEntry> entries = userId == _userId ? [.. _list.Values.OrderBy(e => VndbRules.NumberOf(e.VisualNovelId))] : [];
            return Task.FromResult(new VndbResults<VndbListEntry>(entries, false));
        }
    }

    public Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default)
    {
        VndbRules.RequireId(visualNovelId, 'v');
        VndbRules.RequireValid(update);
        Record(nameof(UpdateEntryAsync), visualNovelId);
        RequireValid(token);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            _list.TryGetValue(visualNovelId, out var current);
            current ??= new VndbListEntry(visualNovelId, _visualNovels.FirstOrDefault(v => v.Id == visualNovelId)?.Title, now, now, null, null, null, null, null, []);

            var vote = Apply(current.Vote, update.Vote);
            DateTimeOffset? voted = update.Vote.Kind switch
            {
                VndbChangeKind.Set => now,
                VndbChangeKind.Clear => null,
                _ => current.Voted,
            };

            // O "Voted" é do servidor: segue o voto e não entra pelos rótulos.
            var labels = current.Labels
                .Where(l => l.Id != VndbRules.VotedLabelId && !update.LabelsUnset.Contains(l.Id))
                .Concat(update.LabelsSet.Where(id => current.Labels.All(l => l.Id != id)).Select(id => new VndbListLabel(id, "#" + id)))
                .ToList();
            if (vote is not null)
            {
                labels.Add(new VndbListLabel(VndbRules.VotedLabelId, "Voted"));
            }

            _list[visualNovelId] = current with
            {
                LastModified = now,
                Vote = vote,
                Voted = voted,
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
        _visualNovels.Where(v => Names(v).Any(name => name.Contains(title, StringComparison.OrdinalIgnoreCase)));

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
