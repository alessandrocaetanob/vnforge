using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Memory;

/// <summary>
/// A memória de tradução entre projetos (#537): os pares aprovados de todos os projetos num arquivo do usuário, <c>translation-memory.json</c> na pasta de configuração
/// (<c>%APPDATA%/VNForge</c>), fora de qualquer projeto. Serve ao inspetor do Editor: dado o original de uma linha, devolve a tradução aprovada em outro projeto para o mesmo
/// original (100%) ou para um parecido (a partir de 80%). Nada grava sozinho na tradução; quem usa decide.
/// </summary>
/// <remarks>
/// O arquivo é JSON, como o resto do estado do VNForge, e a busca roda sobre uma cópia em memória que é relida quando o arquivo muda. A busca exata é por dicionário e a aproximada
/// percorre os pares com uma distância de edição limitada, então o volume de dezenas de projetos não pesa. Se o volume real mostrar o contrário, o arquivo pode virar um banco
/// (SQLite) sem mudar esta API. Só entram pares aprovados; linha "Não traduzir" e linha sem tradução ficam de fora.
/// </remarks>
public sealed class TranslationMemory(string directory)
{
    public const string FileName = "translation-memory.json";

    /// <summary>Quantas correspondências o inspetor mostra por linha.</summary>
    public const int DefaultLimit = 5;

    private readonly UserEntriesFile<MemoryEntry> file = new(System.IO.Path.Combine(directory, FileName));
    private readonly object gate = new();
    private Snapshot? cached;

    public string Path => file.Path;

    /// <summary>Chave de um projeto na memória: a pasta dele, sem a barra final.</summary>
    public static string ProjectKeyOf(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return KeyOf(project.Root);
    }

    /// <summary>Chave de um projeto na memória a partir da pasta.</summary>
    public static string KeyOf(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return System.IO.Path.GetFullPath(projectRoot).TrimEnd('\\', '/');
    }

    /// <exception cref="MemoryFileException">O arquivo existe e não pôde ser lido.</exception>
    public MemoryStats Stats()
    {
        lock (gate)
        {
            var entries = Current().Entries;
            return new MemoryStats(entries.Count, entries.Select(e => e.Entry.ProjectKey).Distinct(KeyComparer).Count());
        }
    }

    /// <summary>Todos os pares da memória, do mais recente ao mais antigo.</summary>
    /// <exception cref="MemoryFileException">O arquivo existe e não pôde ser lido.</exception>
    public IReadOnlyList<MemoryEntry> All()
    {
        lock (gate)
        {
            return Current().Entries.Select(e => e.Entry).OrderByDescending(e => e.ApprovedAt).ToList();
        }
    }

    /// <summary>
    /// As traduções aprovadas em outros projetos para <paramref name="source"/>: primeiro as de original igual (100%), depois as de original parecido, da mais à menos
    /// semelhante e, no empate, da mais recente. As de <paramref name="excludeProjectKey"/> ficam de fora (a linha de outro projeto é que é novidade). O mesmo texto aprovado
    /// duas vezes no mesmo projeto entra uma vez só.
    /// </summary>
    /// <param name="minimum">Semelhança mínima de uma correspondência aproximada, de 0 a 1.</param>
    /// <exception cref="MemoryFileException">O arquivo existe e não pôde ser lido.</exception>
    public IReadOnlyList<MemoryMatch> Find(string source, string? excludeProjectKey = null, double minimum = SourceSimilarity.DefaultMinimum, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(source);
        var normalized = SourceSimilarity.Normalize(source);
        if (normalized.Length == 0 || limit <= 0)
        {
            return [];
        }

        IReadOnlyList<Indexed> entries;
        lock (gate)
        {
            entries = Current().Entries;
        }

        var found = new List<MemoryMatch>();
        foreach (var item in entries)
        {
            if (excludeProjectKey is not null && KeyComparer.Equals(item.Entry.ProjectKey, excludeProjectKey))
            {
                continue;
            }

            var similarity = SourceSimilarity.Compare(normalized, item.Normalized, minimum);
            if (similarity <= 0)
            {
                continue;
            }

            // Só o original igual vale 100%; um parecido nunca passa de 99, mesmo que o arredondamento diga o contrário.
            var exact = string.Equals(normalized, item.Normalized, StringComparison.Ordinal);
            var percent = exact ? 100 : Math.Min(99, (int)Math.Round(similarity * 100, MidpointRounding.AwayFromZero));
            found.Add(new MemoryMatch(item.Entry, percent));
        }

        return found
            .GroupBy(m => (Project: m.Entry.ProjectKey.ToUpperInvariant(), Target: m.Entry.Target.Trim()))
            .Select(g => g.OrderByDescending(m => m.Percent).ThenByDescending(m => m.Entry.ApprovedAt).First())
            .OrderByDescending(m => m.Percent)
            .ThenByDescending(m => m.Entry.ApprovedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Deixa a memória com os pares aprovados de hoje do projeto: o que foi aprovado entra, o que mudou de tradução ou de original é trocado (e conta como aprovado agora) e o
    /// que deixou de estar aprovado sai. Um par que continua igual mantém a data em que entrou. Nada é gravado quando nada mudou.
    /// </summary>
    /// <exception cref="TextFileException">Algum <c>text/*.json</c> do projeto é inválido; a memória não muda.</exception>
    /// <exception cref="MemoryFileException">O arquivo da memória não pôde ser lido ou gravado.</exception>
    public MemorySyncResult SyncProject(ProjectFolder project, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        var key = ProjectKeyOf(project);
        var name = project.LoadConfig().Name;
        var approved = project.LoadAllText().Values
            .SelectMany(units => units)
            .Select(unit => ToEntry(key, name, unit, now))
            .OfType<MemoryEntry>()
            .ToList();

        lock (gate)
        {
            var all = file.Load();
            var before = all.Where(e => KeyComparer.Equals(e.ProjectKey, key)).ToDictionary(e => e.UnitId, StringComparer.Ordinal);
            int added = 0, updated = 0;
            var merged = new List<MemoryEntry>(approved.Count);
            foreach (var entry in approved)
            {
                if (!before.Remove(entry.UnitId, out var old))
                {
                    added++;
                    merged.Add(entry);
                }
                else if (old.Source != entry.Source || old.Target != entry.Target)
                {
                    updated++;
                    merged.Add(entry);
                }
                else
                {
                    merged.Add(old with { ProjectName = name });
                    updated += old.ProjectName == name ? 0 : 1;
                }
            }

            var removed = before.Count;
            if (added + updated + removed > 0)
            {
                Persist(all.Where(e => !KeyComparer.Equals(e.ProjectKey, key)).Concat(merged).ToList());
            }

            return new MemorySyncResult(added, updated, removed, merged.Count);
        }
    }

    /// <summary>
    /// Registra uma linha que acabou de ser gravada: aprovada, entra (ou troca o par antigo da mesma linha); qualquer outro estado tira o par dela da memória.
    /// Devolve se a memória mudou. O Editor chama a cada gravação, para a memória acompanhar a aprovação sem varrer o projeto.
    /// </summary>
    /// <exception cref="MemoryFileException">O arquivo da memória não pôde ser lido ou gravado.</exception>
    public bool Record(string projectKey, string projectName, TextUnit unit, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(unit);
        var entry = ToEntry(projectKey, projectName, unit, now);
        lock (gate)
        {
            var all = file.Load();
            var index = all.FindIndex(e => KeyComparer.Equals(e.ProjectKey, projectKey) && string.Equals(e.UnitId, unit.Id, StringComparison.Ordinal));
            if (entry is null)
            {
                if (index < 0)
                {
                    return false;
                }

                all.RemoveAt(index);
            }
            else if (index < 0)
            {
                all.Add(entry);
            }
            else if (all[index].Source == entry.Source && all[index].Target == entry.Target)
            {
                return false;
            }
            else
            {
                all[index] = entry;
            }

            Persist(all);
            return true;
        }
    }

    /// <summary>Tira da memória todos os pares de um projeto (o projeto foi apagado ou a pessoa não quer mais os pares dele). Devolve quantos saíram.</summary>
    /// <exception cref="MemoryFileException">O arquivo da memória não pôde ser lido ou gravado.</exception>
    public int RemoveProject(string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        lock (gate)
        {
            var all = file.Load();
            var kept = all.Where(e => !KeyComparer.Equals(e.ProjectKey, projectKey)).ToList();
            if (kept.Count == all.Count)
            {
                return 0;
            }

            Persist(kept);
            return all.Count - kept.Count;
        }
    }

    private static StringComparer KeyComparer => StringComparer.OrdinalIgnoreCase;

    private static MemoryEntry? ToEntry(string projectKey, string projectName, TextUnit unit, DateTimeOffset now) =>
        unit.Status == TranslationStatus.Approved
        && !string.IsNullOrWhiteSpace(unit.Translation)
        && SourceSimilarity.Normalize(unit.Source).Length > 0
            ? new MemoryEntry(unit.Source, unit.Translation, projectKey, projectName, unit.Id, now)
            : null;

    private void Persist(List<MemoryEntry> entries)
    {
        file.Save(entries);
        cached = null;
    }

    private Snapshot Current()
    {
        var stamp = file.Stamp();
        if (cached is { } snapshot && snapshot.Stamp == stamp)
        {
            return snapshot;
        }

        var entries = file.Load().Select(e => new Indexed(e, SourceSimilarity.Normalize(e.Source))).ToList();
        cached = new Snapshot(stamp, entries);
        return cached;
    }

    private sealed record Indexed(MemoryEntry Entry, string Normalized);

    private sealed record Snapshot((DateTime Stamp, long Length) Stamp, IReadOnlyList<Indexed> Entries);
}
