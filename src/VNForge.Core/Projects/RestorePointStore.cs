using System.Text;
using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Um ponto de restauração guardado em <c>.cache/pontos-de-restauracao/</c>: o que uma operação em lote mudou, para desfazer depois.</summary>
/// <param name="Id">O nome da pasta do ponto; ordena pela hora em que ele foi criado.</param>
/// <param name="Title">O nome da operação, como a lista de tarefas o mostra.</param>
/// <param name="CreatedAt">Quando a operação terminou.</param>
/// <param name="LineCount">Quantas linhas a operação mudou.</param>
/// <param name="Scripts">As chaves dos scripts que ela tocou.</param>
public sealed record RestorePoint(string Id, string Title, DateTimeOffset CreatedAt, int LineCount, IReadOnlyList<string> Scripts);

/// <summary>O que o "Desfazer" de um ponto fez.</summary>
/// <param name="Restored">Linhas que voltaram ao estado de antes do lote.</param>
/// <param name="EditedSince">Linhas editadas depois do lote: ficam como estão.</param>
/// <param name="Missing">Linhas que já não existem no script (uma nova extração as tirou).</param>
public sealed record RestoreUndoResult(int Restored, int EditedSince, int Missing);

/// <summary>O que uma operação devolveu e o ponto de restauração que ela deixou (nulo quando não mudou linha nenhuma).</summary>
public sealed record RecordedOperation<T>(T Result, RestorePoint? Point);

/// <summary>
/// Os pontos de restauração das operações em lote (tradução da IA, substituição, "corrigir com um clique", "Usar em todos"). Antes da operação, <see cref="Begin"/>
/// guarda o estado dos scripts na memória; ao fim, <see cref="RestorePointRecorder.Complete"/> compara com o disco e grava em <c>.cache/pontos-de-restauracao/&lt;id&gt;/</c>
/// só as linhas que mudaram, antes e depois. <see cref="Undo"/> devolve cada uma ao que era, se ainda estiver como o lote a deixou: a linha editada depois fica como está.
/// Apagar o <c>.cache/</c> só tira o "Desfazer"; nenhuma tradução se perde. Pontos mais velhos que <see cref="Retention"/> saem quando um novo é gravado.
/// </summary>
public sealed class RestorePointStore
{
    /// <summary>Quanto tempo um ponto fica guardado.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    internal const string ManifestFileName = "ponto.json";

    private const int CurrentVersion = 1;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ProjectFolder project;
    private readonly TimeProvider clock;

    public RestorePointStore(ProjectFolder project, TimeProvider? clock = null)
    {
        this.project = project ?? throw new ArgumentNullException(nameof(project));
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>A pasta dos pontos do projeto.</summary>
    public string StoreDirectory => Path.Combine(project.CacheDirectory, "pontos-de-restauracao");

    /// <summary>
    /// Uma linha gravada pela IA e não por uma edição à mão. Serve de <c>include</c> da tradução em lote: a pessoa continua editando enquanto a IA trabalha, e essas
    /// edições não entram no ponto da IA.
    /// </summary>
    public static bool IsAiWrite(TextUnit before, TextUnit after) =>
        after.Origin is { } origin && !string.Equals(origin.Provider, LineEditor.ManualProvider, StringComparison.Ordinal);

    /// <summary>Guarda o estado dos scripts antes da operação. Chame <see cref="RestorePointRecorder.Complete"/> depois dela, mesmo que ela falhe no meio.</summary>
    /// <param name="title">O nome da operação na lista de tarefas.</param>
    /// <param name="scriptKeys">Os scripts que a operação pode mudar; nulo guarda todos.</param>
    /// <param name="include">Decide, linha a linha (antes, depois), se a mudança é da operação; nulo aceita toda linha que mudou.</param>
    public RestorePointRecorder Begin(string title, IEnumerable<string>? scriptKeys = null, Func<TextUnit, TextUnit, bool>? include = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        lock (ProjectTextLock.For(project))
        {
            var keys = scriptKeys is null
                ? project.LoadAllText().Keys.ToList()
                : scriptKeys.Distinct(StringComparer.Ordinal).Where(k => File.Exists(project.TextPath(k))).ToList();
            var snapshot = new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                snapshot[key] = project.LoadText(key);
            }

            return new RestorePointRecorder(this, project, title, snapshot, include);
        }
    }

    /// <summary>Roda a operação entre <see cref="Begin"/> e <see cref="RestorePointRecorder.Complete"/>; se ela falhar, o que ela já gravou ainda vira um ponto.</summary>
    public RecordedOperation<T> Record<T>(string title, Func<T> operation, IEnumerable<string>? scriptKeys = null, Func<TextUnit, TextUnit, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var recorder = Begin(title, scriptKeys, include);
        T result;
        try
        {
            result = operation();
        }
        catch
        {
            TryComplete(recorder);
            throw;
        }

        return new RecordedOperation<T>(result, recorder.Complete());
    }

    /// <summary>Os pontos guardados, do mais novo para o mais antigo. Pasta de ponto incompleta ou ilegível fica de fora.</summary>
    public IReadOnlyList<RestorePoint> List()
    {
        if (!Directory.Exists(StoreDirectory))
        {
            return [];
        }

        var points = new List<RestorePoint>();
        foreach (var folder in Directory.EnumerateDirectories(StoreDirectory))
        {
            if (ReadManifest(folder) is { } point)
            {
                points.Add(point);
            }
        }

        return [.. points.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id, StringComparer.Ordinal)];
    }

    public RestorePoint? Find(string id) => ReadManifest(FolderOf(id));

    /// <summary>
    /// Devolve as linhas do ponto ao que eram antes do lote. Uma linha só volta se a tradução e o status ainda são os que o lote gravou; as outras foram editadas
    /// depois e ficam como estão. As notas, os avisos marcados e o falante escolhido são os de agora. O ponto é apagado ao fim.
    /// </summary>
    /// <exception cref="VNForgeException">O ponto não existe (já foi desfeito ou passou do prazo).</exception>
    /// <exception cref="TextFileException">Um script do projeto ou do ponto está ilegível; nada é gravado nesse script.</exception>
    public RestoreUndoResult Undo(string id)
    {
        var folder = FolderOf(id);
        var point = ReadManifest(folder) ?? throw new VNForgeException("Este ponto de restauração não existe mais. Ele pode já ter sido desfeito ou ter passado do prazo.");
        int restored = 0, edited = 0, missing = 0;
        lock (ProjectTextLock.For(project))
        {
            foreach (var key in point.Scripts)
            {
                var before = ReadUnits(folder, key, "antes").ToDictionary(u => u.Id, StringComparer.Ordinal);
                var after = ReadUnits(folder, key, "depois");
                var units = File.Exists(project.TextPath(key)) ? project.LoadText(key).ToList() : [];
                var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < units.Count; i++)
                {
                    indexById[units[i].Id] = i;
                }

                var changed = false;
                foreach (var written in after)
                {
                    if (!indexById.TryGetValue(written.Id, out var index))
                    {
                        missing++;
                        continue;
                    }

                    var current = units[index];
                    if (!string.Equals(current.Translation, written.Translation, StringComparison.Ordinal) || current.Status != written.Status)
                    {
                        edited++;
                        continue;
                    }

                    var back = before[written.Id] with { Notes = current.Notes, Flags = current.Flags, ChosenSpeaker = current.ChosenSpeaker };
                    units[index] = back;
                    changed = true;
                    restored++;
                }

                if (changed)
                {
                    project.SaveText(key, units);
                }
            }
        }

        Delete(folder);
        return new RestoreUndoResult(restored, edited, missing);
    }

    /// <summary>Apaga o ponto sem desfazer nada.</summary>
    public void Discard(string id) => Delete(FolderOf(id));

    internal RestorePoint? Write(string title, IReadOnlyDictionary<string, (List<TextUnit> Before, List<TextUnit> After)> changes)
    {
        var count = changes.Sum(c => c.Value.After.Count);
        if (count == 0)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var folder = FolderOf(id);
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var (key, (before, after)) in changes)
            {
                File.WriteAllText(UnitsPath(folder, key, "antes"), TextFileSerializer.Serialize(before, UnitsPath(folder, key, "antes")), Utf8);
                File.WriteAllText(UnitsPath(folder, key, "depois"), TextFileSerializer.Serialize(after, UnitsPath(folder, key, "depois")), Utf8);
            }

            var point = new RestorePoint(id, title, now, count, [.. changes.Keys.Order(StringComparer.Ordinal)]);
            var manifest = Path.Combine(folder, ManifestFileName);
            var temporary = $"{manifest}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(new Manifest(CurrentVersion, point.Id, point.Title, point.CreatedAt, point.LineCount, point.Scripts), Json), Utf8);
                ProjectFileIo.Move(temporary, manifest, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }

            Prune(now);
            return point;
        }
        catch
        {
            Delete(folder);
            throw;
        }
    }

    private static void TryComplete(RestorePointRecorder recorder)
    {
        try
        {
            recorder.Complete();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // A operação já falhou; o erro que importa é o dela.
        }
    }

    private void Prune(DateTimeOffset now)
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(StoreDirectory))
            {
                var created = ReadManifest(folder)?.CreatedAt ?? new DateTimeOffset(Directory.GetCreationTimeUtc(folder), TimeSpan.Zero);
                if (now - created > Retention)
                {
                    Delete(folder);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Limpar o que passou do prazo é só zelo: um disco ocupado agora não impede o ponto novo.
        }
    }

    private string FolderOf(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
        {
            throw new VNForgeException("Identificador de ponto de restauração inválido.");
        }

        return Path.Combine(StoreDirectory, id);
    }

    private static string UnitsPath(string folder, string key, string side) => Path.Combine(folder, $"{key}.{side}.json");

    private static IReadOnlyList<TextUnit> ReadUnits(string folder, string key, string side)
    {
        var path = UnitsPath(folder, key, side);
        return TextFileSerializer.Parse(Utf8.GetString(ProjectFileIo.ReadAllBytes(path)), path, key);
    }

    private static RestorePoint? ReadManifest(string folder)
    {
        try
        {
            var manifest = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifest))
            {
                return null;
            }

            var read = JsonSerializer.Deserialize<Manifest>(Utf8.GetString(ProjectFileIo.ReadAllBytes(manifest)), Json);
            return read is { Version: CurrentVersion, Id: not null, Title: not null, Scripts: not null } && string.Equals(read.Id, Path.GetFileName(folder), StringComparison.Ordinal)
                ? new RestorePoint(read.Id, read.Title, read.CreatedAt, read.LineCount, read.Scripts)
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // O ponto já foi usado ou descartado; sobra uma pasta que o prazo limpa depois.
        }
    }

    private sealed record Manifest(int Version, string? Id, string? Title, DateTimeOffset CreatedAt, int LineCount, IReadOnlyList<string>? Scripts);
}

/// <summary>A captura de uma operação em lote: o estado de antes, na memória, até <see cref="Complete"/>.</summary>
public sealed class RestorePointRecorder
{
    private readonly RestorePointStore store;
    private readonly ProjectFolder project;
    private readonly string title;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> snapshot;
    private readonly Func<TextUnit, TextUnit, bool>? include;
    private bool completed;

    internal RestorePointRecorder(
        RestorePointStore store,
        ProjectFolder project,
        string title,
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> snapshot,
        Func<TextUnit, TextUnit, bool>? include)
    {
        this.store = store;
        this.project = project;
        this.title = title;
        this.snapshot = snapshot;
        this.include = include;
    }

    /// <summary>
    /// Compara o disco com o estado de antes e grava o ponto com as linhas que mudaram. Devolve nulo quando nada mudou. Só vale uma vez.
    /// </summary>
    /// <exception cref="InvalidOperationException">Já foi chamado.</exception>
    public RestorePoint? Complete()
    {
        if (completed)
        {
            throw new InvalidOperationException("A captura já foi concluída.");
        }

        completed = true;
        var changes = new Dictionary<string, (List<TextUnit> Before, List<TextUnit> After)>(StringComparer.Ordinal);
        lock (ProjectTextLock.For(project))
        {
            foreach (var (key, before) in snapshot)
            {
                if (!File.Exists(project.TextPath(key)))
                {
                    continue;
                }

                var previous = before.ToDictionary(u => u.Id, StringComparer.Ordinal);
                var pair = (Before: new List<TextUnit>(), After: new List<TextUnit>());
                foreach (var now in project.LoadText(key))
                {
                    if (previous.TryGetValue(now.Id, out var was)
                        && !string.Equals(TextUnitJson.Serialize(was), TextUnitJson.Serialize(now), StringComparison.Ordinal)
                        && (include?.Invoke(was, now) ?? true))
                    {
                        pair.Before.Add(was);
                        pair.After.Add(now);
                    }
                }

                if (pair.After.Count > 0)
                {
                    changes[key] = pair;
                }
            }
        }

        return store.Write(title, changes);
    }
}
