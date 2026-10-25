using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>O que uma gravação de lote fez: o script gravado por último (para o progresso) e quantas linhas ficaram como a pessoa as deixou.</summary>
/// <param name="LastKey">A chave do último script gravado, ou a do lote se nenhum mudou.</param>
/// <param name="Skipped">Linhas que a pessoa mudou depois da seleção (traduziu, aprovou, trocou o original) e que a gravação não tocou.</param>
public readonly record struct LineSaveResult(string LastKey, int Skipped);

/// <summary>
/// As linhas que um comando ou a janela de IA vai processar e o mapa para gravá-las de volta: o conteúdo de cada <c>text/*.json</c> tocado
/// e, por id, a chave do arquivo e o índice da linha. É o mesmo código do <c>vnf translate</c>, do <c>vnf review</c> e da janela "Traduzir com IA".
/// </summary>
/// <remarks>
/// O retrato lido na seleção só serve de contexto para montar os lotes. Cada gravação relê o script, sob a trava do projeto
/// (<see cref="ProjectTextLock"/>, a mesma do <see cref="LineEditor"/>), e só muda a linha que continua como estava na seleção: uma edição ou
/// aprovação feita durante a execução não se perde, e uma tradução feita à mão não é sobrescrita pela IA.
/// </remarks>
public sealed class LineSelection
{
    public const int PreviousLinesWindow = 30;

    public const int NextLinesWindow = 10;

    private readonly Lock gate = new();
    private readonly Dictionary<string, List<TextUnit>> files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> keyById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> indexById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextUnit> selectedById = new(StringComparer.Ordinal);
    private readonly List<TextUnit> pending = [];

    public IReadOnlyList<TextUnit> Pending => pending;

    /// <summary>Quantos scripts têm linha selecionada.</summary>
    public int FileCount => files.Count;

    /// <param name="scriptFilter">Diz se o script de uma chave entra; <c>null</c> aceita todos.</param>
    /// <param name="limit">Máximo de linhas; <c>null</c> sem limite.</param>
    /// <param name="wanted">Quais linhas de um script entram.</param>
    public static LineSelection Build(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts, Func<string, bool>? scriptFilter, int? limit, Func<TextUnit, bool> wanted)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(wanted);
        var selection = new LineSelection();
        foreach (var (key, units) in texts)
        {
            if (scriptFilter is not null && !scriptFilter(key))
            {
                continue;
            }

            var selected = units.Where(wanted).ToList();
            if (selected.Count == 0 || (limit is { } max && selection.pending.Count >= max))
            {
                continue;
            }

            selection.files[key] = [.. units];
            for (var i = 0; i < units.Count; i++)
            {
                selection.keyById[units[i].Id] = key;
                selection.indexById[units[i].Id] = i;
            }

            var taken = limit is { } cap ? selected.Take(cap - selection.pending.Count).ToList() : selected;
            foreach (var unit in taken)
            {
                selection.selectedById[unit.Id] = unit;
            }

            selection.pending.AddRange(taken);
        }

        return selection;
    }

    /// <summary>
    /// Relê os scripts, aplica <paramref name="update"/> a cada resultado cuja linha continua como na seleção (devolve a linha nova, ou <c>null</c>
    /// para deixá-la como está), grava só os <c>text/*.json</c> que mudaram e conta as linhas que a pessoa mudou nesse meio tempo.
    /// </summary>
    /// <exception cref="TextFileException">Algum script ausente ou inválido; nada é gravado.</exception>
    public LineSaveResult ApplyAndSave(ProjectFolder project, IReadOnlyList<TranslationResult> results, Func<TranslationResult, TextUnit, TextUnit?> update)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(update);

        lock (ProjectTextLock.For(project))
        {
            var current = new Dictionary<string, (List<TextUnit> Units, Dictionary<string, int> Index)>(StringComparer.Ordinal);
            var touched = new SortedSet<string>(StringComparer.Ordinal);
            var skipped = 0;
            foreach (var result in results)
            {
                var key = keyById[result.Id];
                if (!current.TryGetValue(key, out var script))
                {
                    var units = project.LoadText(key).ToList();
                    script = (units, units.Select((u, i) => (u.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal));
                    current[key] = script;
                }

                if (!script.Index.TryGetValue(result.Id, out var index) || !StillAsSelected(script.Units[index], selectedById[result.Id]))
                {
                    skipped++;
                    continue;
                }

                if (update(result, script.Units[index]) is { } changed)
                {
                    script.Units[index] = changed;
                    touched.Add(key);
                    Remember(key, result.Id, changed);
                }
            }

            foreach (var key in touched)
            {
                project.SaveText(key, current[key].Units);
            }

            return new LineSaveResult(touched.LastOrDefault() ?? keyById[results[0].Id], skipped);
        }
    }

    /// <summary>Monta o lote com as linhas anteriores e as seguintes do script como contexto; seguro entre threads.</summary>
    public TranslationBatch BuildBatch(TranslationBatchBuilder builder, IReadOnlyList<TextUnit> lines)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var first = lines[0].Id;
        var last = lines[^1].Id;
        lock (gate)
        {
            var previous = files[keyById[first]].Take(indexById[first]).TakeLast(PreviousLinesWindow).ToList();
            var next = files[keyById[last]].Skip(indexById[last] + 1).Take(NextLinesWindow).ToList();
            return builder.Build(lines, previous, next);
        }
    }

    private static bool StillAsSelected(TextUnit now, TextUnit selected) =>
        now.Status == selected.Status
        && string.Equals(now.Source, selected.Source, StringComparison.Ordinal)
        && string.Equals(now.Translation, selected.Translation, StringComparison.Ordinal);

    private void Remember(string key, string id, TextUnit changed)
    {
        lock (gate)
        {
            files[key][indexById[id]] = changed;
        }
    }
}
