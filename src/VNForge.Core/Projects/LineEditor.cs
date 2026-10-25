using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// Edição manual de uma linha de <c>text/&lt;script&gt;.json</c>: tradução, notas e aprovação. Cada operação lê o script, troca a unidade pelo
/// <c>id</c> e grava de volta com <see cref="ProjectFolder.SaveText"/> (atômico); se nada mudou, o arquivo não é regravado.
/// </summary>
public sealed class LineEditor(ProjectFolder project, TimeProvider clock)
{
    internal const string ManualProvider = "manual";

    private readonly ProjectFolder project = project ?? throw new ArgumentNullException(nameof(project));
    private readonly TimeProvider clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Grava a tradução digitada. Texto nulo, vazio ou só de espaços deixa a linha sem tradução (<see cref="TranslationStatus.Untranslated"/>, sem origem).
    /// Texto novo vira <see cref="TranslationStatus.Translated"/> com origem <c>manual</c> no horário do relógio, mesmo que a linha estivesse revisada ou aprovada:
    /// editar desfaz a revisão. Texto igual ao gravado não muda nada.
    /// </summary>
    /// <returns>A unidade como ficou no arquivo.</returns>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    /// <exception cref="TextFileException">Arquivo do script ausente ou inválido.</exception>
    public TextUnit SetTranslation(string scriptKey, string id, string? translation) =>
        Update(scriptKey, id, unit =>
        {
            if (string.IsNullOrWhiteSpace(translation))
            {
                return string.IsNullOrEmpty(unit.Translation) && unit.Status == TranslationStatus.Untranslated && unit.Origin is null
                    ? unit
                    : unit with { Translation = null, Status = TranslationStatus.Untranslated, Origin = null };
            }

            return string.Equals(unit.Translation, translation, StringComparison.Ordinal)
                ? unit
                : unit with
                {
                    Translation = translation,
                    Status = TranslationStatus.Translated,
                    Origin = new TranslationOrigin(ManualProvider, null, clock.GetUtcNow()),
                };
        });

    /// <summary>
    /// "Usar em todos": grava <paramref name="translation"/> nas linhas indicadas, como <see cref="SetTranslation"/> faria em cada uma, com um arquivo gravado por script.
    /// Linhas que já têm exatamente esse texto ficam como estão e não voltam na lista. Se alguma linha não existir, nada é gravado.
    /// </summary>
    /// <returns>As unidades que mudaram, na ordem de <paramref name="targets"/>.</returns>
    /// <exception cref="KeyNotFoundException">Alguma linha não existe no script indicado.</exception>
    public IReadOnlyList<TextUnit> SetTranslationFor(IEnumerable<(string ScriptKey, string Id)> targets, string translation)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(translation);

        var origin = new TranslationOrigin(ManualProvider, null, clock.GetUtcNow());
        var scripts = new Dictionary<string, List<TextUnit>>(StringComparer.Ordinal);
        var changedIds = new List<(string ScriptKey, string Id)>();
        var changed = new Dictionary<(string, string), TextUnit>();
        foreach (var (scriptKey, id) in targets)
        {
            if (!scripts.TryGetValue(scriptKey, out var units))
            {
                scripts[scriptKey] = units = [.. project.LoadText(scriptKey)];
            }

            var index = units.FindIndex(u => string.Equals(u.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new KeyNotFoundException($"A linha '{id}' não existe no script '{scriptKey}'. Reabra o projeto e tente de novo.");
            }

            if (string.Equals(units[index].Translation, translation, StringComparison.Ordinal))
            {
                continue;
            }

            units[index] = units[index] with { Translation = translation, Status = TranslationStatus.Translated, Origin = origin };
            changedIds.Add((scriptKey, id));
            changed[(scriptKey, id)] = units[index];
        }

        foreach (var scriptKey in changedIds.Select(c => c.ScriptKey).Distinct(StringComparer.Ordinal))
        {
            project.SaveText(scriptKey, scripts[scriptKey]);
        }

        return [.. changedIds.Select(c => changed[c])];
    }

    /// <summary>Grava as notas da linha, sem mexer na tradução nem no status.</summary>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    public TextUnit SetNotes(string scriptKey, string id, string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return Update(scriptKey, id, unit => string.Equals(unit.Notes, notes, StringComparison.Ordinal) ? unit : unit with { Notes = notes });
    }

    /// <summary>Marca a linha como <see cref="TranslationStatus.Approved"/>, mantendo a origem. Exige texto de tradução na linha.</summary>
    /// <exception cref="InvalidOperationException">A linha não tem tradução.</exception>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    public TextUnit Approve(string scriptKey, string id) =>
        Update(scriptKey, id, unit =>
        {
            if (string.IsNullOrWhiteSpace(unit.Translation))
            {
                throw new InvalidOperationException($"A linha '{unit.Id}' não tem tradução e não pode ser aprovada. Escreva a tradução antes de aprovar.");
            }

            return unit.Status == TranslationStatus.Approved ? unit : unit with { Status = TranslationStatus.Approved };
        });

    /// <summary>
    /// Devolve a linha ao estado que ela tinha (tradução, status e origem), p. ex. para desfazer uma gravação. Só desfaz se o arquivo ainda guarda o que
    /// <paramref name="saved"/> gravou; as notas, os avisos marcados e o original a revisar são os de agora, não os do instantâneo.
    /// </summary>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com o id de <paramref name="previous"/>.</exception>
    /// <exception cref="VNForgeException">A linha mudou depois da gravação desfeita.</exception>
    public TextUnit Restore(string scriptKey, TextUnit previous, TextUnit saved)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(saved);
        return Update(scriptKey, previous.Id, unit =>
        {
            if (!string.Equals(unit.Translation, saved.Translation, StringComparison.Ordinal) || unit.Status != saved.Status)
            {
                throw new VNForgeException("A linha mudou depois deste aviso; nada foi desfeito.");
            }

            var restored = unit with { Translation = previous.Translation, Status = previous.Status, Origin = previous.Origin };
            return restored == unit ? unit : restored;
        });
    }

    private TextUnit Update(string scriptKey, string id, Func<TextUnit, TextUnit> change)
    {
        ArgumentNullException.ThrowIfNull(id);

        var units = project.LoadText(scriptKey);
        var index = -1;
        for (var i = 0; i < units.Count; i++)
        {
            if (string.Equals(units[i].Id, id, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new KeyNotFoundException($"A linha '{id}' não existe no script '{scriptKey}'. Reabra o projeto e tente de novo.");
        }

        var current = units[index];
        var updated = change(current);
        if (ReferenceEquals(updated, current))
        {
            return current;
        }

        var all = units.ToList();
        all[index] = updated;
        project.SaveText(scriptKey, all);
        return updated;
    }
}
