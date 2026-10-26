using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// Edição manual de uma linha de <c>text/&lt;script&gt;.json</c>: tradução, notas e aprovação. Cada operação lê o script, troca a unidade pelo
/// <c>id</c> e grava de volta com <see cref="ProjectFolder.SaveText"/> (atômico); se nada mudou, o arquivo não é regravado.
/// </summary>
public sealed class LineEditor
{
    internal const string ManualProvider = "manual";

    private readonly ProjectFolder project;
    private readonly TimeProvider clock;
    private readonly Action<string, IReadOnlyList<TextUnit>> save;

    public LineEditor(ProjectFolder project, TimeProvider clock)
        : this(project, clock, (key, units) => project.SaveText(key, units))
    {
    }

    /// <param name="project">A pasta do projeto.</param>
    /// <param name="clock">O relógio da origem <c>manual</c>.</param>
    /// <param name="save">Como um script é gravado; o padrão é <see cref="ProjectFolder.SaveText"/>. Os testes passam uma gravação que falha.</param>
    public LineEditor(ProjectFolder project, TimeProvider clock, Action<string, IReadOnlyList<TextUnit>> save)
    {
        this.project = project ?? throw new ArgumentNullException(nameof(project));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
    }

    /// <summary>
    /// Grava a tradução digitada. Texto nulo, vazio ou só de espaços deixa a linha sem tradução (<see cref="TranslationStatus.Untranslated"/>, sem origem).
    /// Texto novo vira <see cref="TranslationStatus.Translated"/> com origem <c>manual</c> no horário do relógio, mesmo que a linha estivesse revisada ou aprovada:
    /// editar desfaz a revisão. Texto igual ao gravado não muda nada.
    /// Com a opção <see cref="StyleConfig.KeyboardPunctuation"/> ligada, a pontuação japonesa do texto (<c>。 ～ （</c>) é trocada pela do teclado antes de gravar.
    /// </summary>
    /// <returns>A unidade como ficou no arquivo.</returns>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    /// <exception cref="TextFileException">Arquivo do script ausente ou inválido.</exception>
    public TextUnit SetTranslation(string scriptKey, string id, string? translation)
    {
        translation = translation is null ? null : KeyboardPunctuation.For(project)?.Apply(translation) ?? translation;
        return Update(scriptKey, id, unit =>
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
    }

    /// <summary>
    /// "Usar em todos": grava <paramref name="translation"/> nas linhas indicadas, como <see cref="SetTranslation"/> faria em cada uma, com um arquivo gravado por script.
    /// Linhas que já têm exatamente esse texto ficam como estão (vêm em <see cref="BatchTranslationResult.Unchanged"/>, para quem mostra a lista se atualizar numa nova tentativa).
    /// Se um script não grava, os anteriores já estão no disco: o resultado traz o que foi gravado e a falha, em vez de perder a lista.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Alguma linha não existe no script indicado; nesse caso nada é gravado.</exception>
    public BatchTranslationResult SetTranslationFor(IEnumerable<(string ScriptKey, string Id)> targets, string translation)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(translation);
        translation = KeyboardPunctuation.For(project)?.Apply(translation) ?? translation;

        lock (ProjectTextLock.For(project))
        {
            return SetTranslationForLocked(targets, translation);
        }
    }

    private BatchTranslationResult SetTranslationForLocked(IEnumerable<(string ScriptKey, string Id)> targets, string translation)
    {
        var origin = new TranslationOrigin(ManualProvider, null, clock.GetUtcNow());
        var scripts = new Dictionary<string, List<TextUnit>>(StringComparer.Ordinal);
        var pending = new Dictionary<string, List<LineChange>>(StringComparer.Ordinal);
        var unchanged = new List<(string ScriptKey, TextUnit Unit)>();
        var requested = 0;
        foreach (var (scriptKey, id) in targets)
        {
            requested++;
            if (!scripts.TryGetValue(scriptKey, out var units))
            {
                scripts[scriptKey] = units = [.. project.LoadText(scriptKey)];
            }

            var index = units.FindIndex(u => string.Equals(u.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new KeyNotFoundException($"A linha '{id}' não existe no script '{scriptKey}'. Reabra o projeto e tente de novo.");
            }

            var previous = units[index];
            if (string.Equals(previous.Translation, translation, StringComparison.Ordinal))
            {
                unchanged.Add((scriptKey, previous));
                continue;
            }

            units[index] = previous with { Translation = translation, Status = TranslationStatus.Translated, Origin = origin };
            if (!pending.TryGetValue(scriptKey, out var list))
            {
                pending[scriptKey] = list = [];
            }

            list.Add(new LineChange(scriptKey, previous, units[index]));
        }

        var saved = new List<LineChange>();
        foreach (var (scriptKey, changes) in pending)
        {
            try
            {
                save(scriptKey, scripts[scriptKey]);
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                return new BatchTranslationResult(saved, unchanged, requested, e);
            }

            saved.AddRange(changes);
        }

        return new BatchTranslationResult(saved, unchanged, requested, null);
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

        lock (ProjectTextLock.For(project))
        {
            return UpdateLocked(scriptKey, id, change);
        }
    }

    private TextUnit UpdateLocked(string scriptKey, string id, Func<TextUnit, TextUnit> change)
    {
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

/// <summary>Uma linha trocada pelo "Usar em todos": a unidade de antes e a de depois, para desfazer.</summary>
public sealed record LineChange(string ScriptKey, TextUnit Previous, TextUnit Saved);

/// <summary>O que o "Usar em todos" fez.</summary>
/// <param name="Changes">As linhas gravadas agora.</param>
/// <param name="Unchanged">As linhas que já tinham exatamente o texto (de uma tentativa anterior, por exemplo).</param>
/// <param name="Requested">Quantas linhas foram pedidas.</param>
/// <param name="Failure">Por que um script não gravou; nulo quando tudo foi gravado.</param>
public sealed record BatchTranslationResult(IReadOnlyList<LineChange> Changes, IReadOnlyList<(string ScriptKey, TextUnit Unit)> Unchanged, int Requested, Exception? Failure)
{
    /// <summary>Quantas linhas passam a ter o texto: as gravadas agora e as que já o tinham.</summary>
    public int Applied => Changes.Count + Unchanged.Count;
}
