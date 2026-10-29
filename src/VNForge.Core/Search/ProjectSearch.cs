using System.Text;
using System.Text.RegularExpressions;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Search;

/// <summary>
/// Busca e substituição em todos os <c>text/*.json</c> do projeto. A busca só lê. A substituição é em duas etapas: <see cref="PreviewReplace(ProjectFolder, SearchOptions, string, CancellationToken)"/>
/// calcula cada linha alterada sem gravar nada, e <see cref="Apply"/> grava exatamente essa prévia.
/// </summary>
/// <remarks>
/// Marcadores de controle (<c>\n</c>, <c>\p</c>, <c>{...}</c>) não são texto: uma ocorrência que encosta num deles não conta, nem na busca nem na substituição
/// (na substituição ela é contada em <see cref="ReplacePreview.ProtectedMatches"/>), e uma troca que deixaria um marcador malformado faz a linha ficar como está.
/// Ocorrências vazias (um regex como <c>x*</c> ou <c>^</c>) são ignoradas.
/// </remarks>
public static class ProjectSearch
{
    /// <summary>Procura nos textos de <paramref name="project"/>.</summary>
    /// <exception cref="SearchException">Texto vazio, regex inválida ou lenta demais.</exception>
    /// <exception cref="TextFileException">Algum <c>text/*.json</c> inválido.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado.</exception>
    public static SearchResult Find(ProjectFolder project, SearchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Find(project.LoadAllText(), options, cancellationToken);
    }

    /// <summary>Procura em <paramref name="texts"/> (script e linhas), na ordem ordinal da chave e na ordem do arquivo.</summary>
    /// <exception cref="SearchException">Texto vazio, regex inválida ou lenta demais.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado; o token é conferido a cada linha.</exception>
    public static SearchResult Find(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, SearchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var pattern = SearchPattern.Compile(options);

        var hits = new List<SearchHit>();
        var emptyLines = 0;
        foreach (var (key, units) in OrderedScripts(texts))
        {
            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = TextToSearch(units[i], options.Field);
                if (text is null)
                {
                    continue;
                }

                var markers = MarkerSpans(text) ?? [];
                var matches = pattern.Matches(text, markers, out var empty);
                var visible = matches.Where(m => !Touches(markers, m)).ToList();
                if (visible.Count > 0)
                {
                    hits.Add(new SearchHit(key, i + 1, units[i].Id, text, [.. visible.Select(m => new TextRange(m.Index, m.Length))]));
                }
                else if (matches.Count == 0 && empty > 0)
                {
                    emptyLines++;
                }
            }
        }

        return new SearchResult(hits, emptyLines);
    }

    /// <summary>Calcula a substituição nos textos de <paramref name="project"/> sem gravar nada.</summary>
    /// <exception cref="SearchException">Texto vazio, regex inválida ou lenta demais, ou o campo é o original.</exception>
    /// <exception cref="TextFileException">Algum <c>text/*.json</c> inválido.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado.</exception>
    public static ReplacePreview PreviewReplace(ProjectFolder project, SearchOptions options, string replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return PreviewReplace(project.LoadAllText(), options, replacement, cancellationToken);
    }

    /// <summary>
    /// Calcula a substituição em <paramref name="texts"/> sem gravar nada. Em regex, <paramref name="replacement"/> aceita <c>$1</c>, <c>${nome}</c> e <c>$$</c>;
    /// no modo literal vale como está.
    /// </summary>
    /// <exception cref="SearchException">Texto vazio, regex inválida ou lenta demais, ou o campo é o original.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado; o token é conferido a cada linha.</exception>
    public static ReplacePreview PreviewReplace(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, SearchOptions options, string replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(replacement);
        if (options.Field != SearchField.Translation)
        {
            throw new SearchException("Só a tradução pode ser substituída. O original do jogo não se altera.");
        }

        var pattern = SearchPattern.Compile(options);
        var items = new List<ReplaceItem>();
        var skipped = new List<SkippedLine>();
        var protectedMatches = 0;
        var emptyLines = 0;
        foreach (var (key, units) in OrderedScripts(texts))
        {
            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = TextToSearch(units[i], SearchField.Translation);
                if (before is null)
                {
                    continue;
                }

                var markers = MarkerSpans(before);
                var matches = pattern.Matches(before, markers ?? [], out var empty);
                if (matches.Count == 0)
                {
                    emptyLines += empty > 0 ? 1 : 0;
                    continue;
                }

                if (markers is null)
                {
                    skipped.Add(new SkippedLine(key, i + 1, SkipReason.MalformedMarkers));
                    continue;
                }

                var outcome = Rewrite(pattern, before, matches, markers, replacement);
                if (outcome.BreaksMarkers)
                {
                    skipped.Add(new SkippedLine(key, i + 1, SkipReason.ReplacementBreaksMarkers));
                    continue;
                }

                protectedMatches += outcome.Protected;
                if (outcome.After is not null)
                {
                    items.Add(new ReplaceItem(key, i + 1, units[i].Id, before, outcome.After, outcome.Count, units[i].Status));
                }
            }
        }

        return new ReplacePreview(options, replacement, items, protectedMatches, skipped, emptyLines);
    }

    /// <summary>
    /// Grava a prévia: cada linha recebe <see cref="ReplaceItem.After"/>, vira <see cref="TranslationStatus.Translated"/> com origem <c>manual</c>
    /// (como uma edição no Editor, que desfaz a revisão) e o script é regravado de uma vez. Uma linha cuja tradução já não é a da prévia não é tocada.
    /// </summary>
    /// <exception cref="TextFileException">Algum script da prévia ausente ou inválido; os scripts anteriores já estão gravados.</exception>
    public static ReplaceResult Apply(ProjectFolder project, ReplacePreview preview, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preview);
        var now = (clock ?? TimeProvider.System).GetUtcNow();

        var applied = 0;
        var stale = new List<ReplaceItem>();
        lock (ProjectTextLock.For(project))
        {
            foreach (var script in preview.Items.GroupBy(i => i.ScriptKey, StringComparer.Ordinal))
            {
                var units = project.LoadText(script.Key).ToList();
                var indexById = new Dictionary<string, int>(units.Count, StringComparer.Ordinal);
                for (var i = 0; i < units.Count; i++)
                {
                    indexById[units[i].Id] = i;
                }

                var changed = false;
                foreach (var item in script)
                {
                    if (!indexById.TryGetValue(item.UnitId, out var index) || !string.Equals(units[index].Translation, item.Before, StringComparison.Ordinal) || !units[index].HasTranslation)
                    {
                        stale.Add(item);
                        continue;
                    }

                    units[index] = units[index] with
                    {
                        Translation = item.After,
                        Status = TranslationStatus.Translated,
                        Origin = new TranslationOrigin(LineEditor.ManualProvider, null, now),
                        Doubt = null,
                        AiTranslation = units[index].AiTranslationAfterEdit(item.After),
                    };
                    applied++;
                    changed = true;
                }

                if (changed)
                {
                    project.SaveText(script.Key, units);
                }
            }
        }

        return new ReplaceResult(applied, stale);
    }

    private readonly record struct Rewritten(string? After, int Count, int Protected, bool BreaksMarkers);

    private static Rewritten Rewrite(SearchPattern pattern, string before, IReadOnlyList<Match> matches, IReadOnlyList<TextRange> markers, string replacement)
    {
        var builder = new StringBuilder(before.Length + 16);
        var position = 0;
        var count = 0;
        var protectedCount = 0;
        foreach (var match in matches)
        {
            if (Touches(markers, match))
            {
                protectedCount++;
                continue;
            }

            var text = pattern.Expand(match, replacement);
            if (!HasValidMarkers(text))
            {
                return new Rewritten(null, 0, 0, BreaksMarkers: true);
            }

            builder.Append(before, position, match.Index - position).Append(text);
            position = match.Index + match.Length;
            count++;
        }

        if (count == 0)
        {
            return new Rewritten(null, 0, protectedCount, BreaksMarkers: false);
        }

        builder.Append(before, position, before.Length - position);
        var after = builder.ToString();
        return new Rewritten(string.Equals(after, before, StringComparison.Ordinal) ? null : after, count, protectedCount, BreaksMarkers: false);
    }

    private static bool Touches(IReadOnlyList<TextRange> markers, Match match)
    {
        foreach (var marker in markers)
        {
            if (match.Index < marker.Start + marker.Length && match.Index + match.Length > marker.Start)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Onde estão os marcadores de <paramref name="text"/>; nulo quando o texto tem um marcador malformado e não dá para saber.</summary>
    private static List<TextRange>? MarkerSpans(string text)
    {
        var spans = new List<TextRange>();
        if (text.AsSpan().IndexOfAny('\\', '{') < 0)
        {
            return spans;
        }

        try
        {
            var position = 0;
            foreach (var segment in ControlMarkers.Split(text))
            {
                if (segment.IsMarker)
                {
                    spans.Add(new TextRange(position, segment.Text.Length));
                }

                position += segment.Text.Length;
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        return spans;
    }

    private static bool HasValidMarkers(string text)
    {
        if (text.AsSpan().IndexOfAny('\\', '{') < 0)
        {
            return true;
        }

        try
        {
            ControlMarkers.Split(text);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? TextToSearch(TextUnit unit, SearchField field)
    {
        if (field == SearchField.Source)
        {
            return string.IsNullOrEmpty(unit.Source) ? null : unit.Source;
        }

        return unit.HasTranslation ? unit.Translation : null;
    }

    private static IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> OrderedScripts(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts) =>
        texts.OrderBy(t => t.Key, StringComparer.Ordinal);
}
