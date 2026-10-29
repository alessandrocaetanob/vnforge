using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>O que a re-extração fez com as linhas que já estavam em <c>text/</c>.</summary>
/// <param name="Kept">Linhas com o mesmo id e o mesmo original: a tradução continua como estava.</param>
/// <param name="Outdated">Linhas com o mesmo id e original diferente. Se tinham tradução, ficam marcadas para revisão (<see cref="TextUnit.PreviousSource"/>).</param>
/// <param name="Added">Linhas novas no jogo.</param>
/// <param name="Removed">Linhas que o jogo não tem mais, inclusive as de scripts que sumiram. O texto delas fica em <see cref="ProjectFolder.TextBackupDirectory"/>.</param>
public sealed record ReextractionSummary(int Kept, int Outdated, int Added, int Removed);

internal sealed record TextMergePlan(
    IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Removed,
    ReextractionSummary Summary);

/// <summary>Junta o texto recém-extraído com o <c>text/*.json</c> existente, casando as linhas pelo id e conferindo o original.</summary>
internal static class TextReextraction
{
    public static TextMergePlan Merge(
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> existing,
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> extracted)
    {
        int kept = 0, outdated = 0, added = 0, removed = 0;
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        var changed = new List<string>();

        foreach (var (key, fresh) in extracted)
        {
            if (!existing.TryGetValue(key, out var old))
            {
                texts[key] = fresh;
                added += fresh.Count;
                changed.Add(key);
                continue;
            }

            var byId = old.ToDictionary(u => u.Id, StringComparer.Ordinal);
            var merged = new List<TextUnit>(fresh.Count);
            foreach (var unit in fresh)
            {
                if (!byId.Remove(unit.Id, out var saved))
                {
                    merged.Add(unit);
                    added++;
                }
                else if (string.Equals(saved.Source, unit.Source, StringComparison.Ordinal))
                {
                    merged.Add(saved with
                    {
                        Kind = unit.Kind,
                        Speaker = unit.Speaker,
                        Locator = unit.Locator,
                        PreviousSource = saved.Status == TranslationStatus.Untranslated ? saved.PreviousSource : null,
                    });
                    kept++;
                }
                else
                {
                    merged.Add(Outdate(saved, unit));
                    outdated++;
                }
            }

            removed += byId.Count;
            texts[key] = merged;
            if (TextUnitJson.SerializeList(merged) != TextUnitJson.SerializeList(old))
            {
                changed.Add(key);
            }
        }

        var gone = existing.Keys.Where(k => !extracted.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        removed += gone.Sum(k => existing[k].Count);
        return new TextMergePlan(texts, changed, gone, new ReextractionSummary(kept, outdated, added, removed));
    }

    /// <summary>
    /// O original mudou. A linha traduzida volta para <see cref="TranslationStatus.Untranslated"/> com a tradução antiga ainda nela
    /// (o build a ignora, e a regra de QA "tradução ignorada" a aponta) e com o original antigo em <see cref="TextUnit.PreviousSource"/>.
    /// </summary>
    private static TextUnit Outdate(TextUnit saved, TextUnit fresh)
    {
        var pending = saved.Status == TranslationStatus.Untranslated ? saved.PreviousSource : null;
        var hadTranslation = !string.IsNullOrEmpty(saved.Translation);
        return fresh with
        {
            Translation = saved.Translation,
            Origin = saved.Origin,
            AiTranslation = saved.AiTranslation,
            Notes = saved.Notes,
            Flags = saved.Flags,
            ChosenSpeaker = saved.ChosenSpeaker,
            PreviousSource = hadTranslation || pending is not null ? pending ?? saved.Source : null,
        };
    }
}
