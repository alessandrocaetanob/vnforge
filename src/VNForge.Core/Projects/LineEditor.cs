using System.Diagnostics;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// Edição manual de uma linha de <c>text/&lt;script&gt;.json</c>: tradução, notas, falante e aprovação. Cada operação lê o script, troca a unidade pelo
/// <c>id</c> e grava de volta com <see cref="ProjectFolder.SaveText"/> (atômico); se nada mudou, o arquivo não é regravado.
/// </summary>
public sealed class LineEditor
{
    internal const string ManualProvider = "manual";

    private readonly ProjectFolder project;
    private readonly TimeProvider clock;
    private readonly Action<string, IReadOnlyList<TextUnit>> save;
    private readonly object punctuationGate = new();
    private (DateTime Stamp, long Length, KeyboardPunctuation? Value)? punctuation;

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
        translation = translation is null ? null : PunctuationOption()?.Apply(translation) ?? translation;
        var saved = Update(scriptKey, id, unit =>
        {
            if (string.IsNullOrWhiteSpace(translation))
            {
                if (unit.Status == TranslationStatus.Keep)
                {
                    return unit;
                }

                return string.IsNullOrEmpty(unit.Translation) && unit.Status == TranslationStatus.Untranslated && unit.Origin is null
                    ? unit
                    : unit with { Translation = null, Status = TranslationStatus.Untranslated, Origin = null, Doubt = null };
            }

            return string.Equals(unit.Translation, translation, StringComparison.Ordinal)
                ? unit
                : unit with
                {
                    Translation = translation,
                    Status = TranslationStatus.Translated,
                    Origin = new TranslationOrigin(ManualProvider, null, clock.GetUtcNow()),
                    Doubt = null,
                };
        });

        if (saved.Kind == TextUnitKind.Name && saved.HasTranslation)
        {
            ShareNameTranslation(saved);
        }

        return saved;
    }

    /// <summary>
    /// Um nome do jogo tem uma só tradução (#426): a que acabou de ser gravada na unidade <c>name</c> vai também para o personagem do mesmo original em
    /// <c>characters.json</c> (se o arquivo existe; quem o cria é a página Glossário) e para as outras unidades <c>name</c> desse original.
    /// A linha já está gravada: um <c>characters.json</c> inválido ou um script ilegível não a desfazem, e a página Glossário refaz a ligação quando abrir.
    /// </summary>
    private void ShareNameTranslation(TextUnit unit)
    {
        var translation = unit.Translation!;
        lock (ProjectTextLock.For(project))
        {
            if (File.Exists(project.CharactersPath))
            {
                try
                {
                    var characters = project.LoadCharacters().ToList();
                    var current = new CharacterBook(characters).Find(unit.Source);
                    if (current is not null && !string.Equals(current.Target, translation, StringComparison.Ordinal))
                    {
                        characters[characters.IndexOf(current)] = current with { Target = translation };
                        project.SaveCharacters(characters);
                    }
                }
                catch (Exception e) when (e is GlossaryFileException or IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine(e);
                }
            }

            try
            {
                var same = new CharacterBook([new CharacterEntry(unit.Source, translation)]);
                var others = project.LoadAllText()
                    .SelectMany(script => script.Value.Select(u => (ScriptKey: script.Key, Unit: u)))
                    .Where(x => x.Unit.Kind == TextUnitKind.Name && x.Unit.Status != TranslationStatus.Keep && x.Unit.Id != unit.Id && same.Find(x.Unit.Source) is not null)
                    .Select(x => (x.ScriptKey, x.Unit.Id))
                    .ToList();
                if (others.Count > 0)
                {
                    var result = SetTranslationForLocked(others, translation);
                    if (result.Failure is not null)
                    {
                        Debug.WriteLine(result.Failure);
                    }
                }
            }
            catch (Exception e) when (e is TextFileException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(e);
            }
        }
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
        translation = PunctuationOption()?.Apply(translation) ?? translation;

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

            units[index] = previous with { Translation = translation, Status = TranslationStatus.Translated, Origin = origin, Doubt = null };
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

    /// <summary>
    /// "Não traduzir": marca a linha como <see cref="TranslationStatus.Keep"/> (<paramref name="keep"/> verdadeiro), sem tradução nem origem, ou a devolve a
    /// <see cref="TranslationStatus.Untranslated"/> (falso). Desfazer só mexe em linha <c>keep</c>; as outras ficam como estão. Linha já no estado pedido não regrava o arquivo.
    /// </summary>
    /// <returns>A unidade como ficou no arquivo.</returns>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    public TextUnit SetKeep(string scriptKey, string id, bool keep) => Update(scriptKey, id, unit => WithKeep(unit, keep));

    /// <summary>
    /// <see cref="SetKeep(string, string, bool)"/> para várias linhas de um script, com uma só gravação do arquivo. Devolve só as linhas que mudaram.
    /// Se alguma não existe, nada é gravado.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Alguma linha não existe no script.</exception>
    public IReadOnlyList<LineChange> SetKeepMany(string scriptKey, IEnumerable<string> ids, bool keep)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToHashSet(StringComparer.Ordinal);

        lock (ProjectTextLock.For(project))
        {
            var units = project.LoadText(scriptKey).ToList();
            var missing = wanted.Except(units.Select(u => u.Id), StringComparer.Ordinal).FirstOrDefault();
            if (missing is not null)
            {
                throw new KeyNotFoundException($"A linha '{missing}' não existe no script '{scriptKey}'. Reabra o projeto e tente de novo.");
            }

            var changes = new List<LineChange>();
            for (var i = 0; i < units.Count; i++)
            {
                if (!wanted.Contains(units[i].Id))
                {
                    continue;
                }

                var updated = WithKeep(units[i], keep);
                if (!ReferenceEquals(updated, units[i]))
                {
                    changes.Add(new LineChange(scriptKey, units[i], updated));
                    units[i] = updated;
                }
            }

            if (changes.Count > 0)
            {
                save(scriptKey, units);
            }

            return changes;
        }
    }

    private static TextUnit WithKeep(TextUnit unit, bool keep)
    {
        var isKeep = unit.Status == TranslationStatus.Keep;
        if (keep)
        {
            return isKeep ? unit : unit with { Status = TranslationStatus.Keep, Translation = null, Origin = null, PreviousSource = null, Doubt = null };
        }

        return isKeep ? unit with { Status = TranslationStatus.Untranslated } : unit;
    }

    /// <summary>Grava as notas da linha, sem mexer na tradução nem no status.</summary>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    public TextUnit SetNotes(string scriptKey, string id, string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return Update(scriptKey, id, unit => string.Equals(unit.Notes, notes, StringComparison.Ordinal) ? unit : unit with { Notes = notes });
    }

    /// <summary>
    /// Grava o falante escolhido da linha (<see cref="TextUnit.ChosenSpeaker"/>), sem mexer na tradução, no status nem no falante extraído. Nulo devolve a linha ao falante do jogo;
    /// vazio (ou só espaços) é "Narração"; um nome igual ao que o jogo já traz (ou narração numa linha que já não tem falante) também volta ao do jogo, para o arquivo
    /// não guardar uma escolha que não muda nada.
    /// </summary>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com esse <paramref name="id"/>.</exception>
    public TextUnit SetChosenSpeaker(string scriptKey, string id, string? speaker) => Update(scriptKey, id, unit => WithChosenSpeaker(unit, speaker));

    /// <summary>
    /// <see cref="SetChosenSpeaker"/> para várias linhas de um script, com uma só gravação do arquivo. Devolve só as linhas que mudaram. Se alguma não existe, nada é gravado.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Alguma linha não existe no script.</exception>
    public IReadOnlyList<LineChange> SetChosenSpeakerMany(string scriptKey, IEnumerable<string> ids, string? speaker)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToHashSet(StringComparer.Ordinal);

        lock (ProjectTextLock.For(project))
        {
            var units = project.LoadText(scriptKey).ToList();
            var missing = wanted.Except(units.Select(u => u.Id), StringComparer.Ordinal).FirstOrDefault();
            if (missing is not null)
            {
                throw new KeyNotFoundException($"A linha '{missing}' não existe no script '{scriptKey}'. Reabra o projeto e tente de novo.");
            }

            var changes = new List<LineChange>();
            for (var i = 0; i < units.Count; i++)
            {
                if (!wanted.Contains(units[i].Id))
                {
                    continue;
                }

                var updated = WithChosenSpeaker(units[i], speaker);
                if (!ReferenceEquals(updated, units[i]))
                {
                    changes.Add(new LineChange(scriptKey, units[i], updated));
                    units[i] = updated;
                }
            }

            if (changes.Count > 0)
            {
                save(scriptKey, units);
            }

            return changes;
        }
    }

    private static TextUnit WithChosenSpeaker(TextUnit unit, string? speaker)
    {
        var chosen = speaker?.Trim();
        if (chosen is not null && string.Equals(chosen, unit.Speaker ?? string.Empty, StringComparison.Ordinal))
        {
            chosen = null;
        }

        return string.Equals(unit.ChosenSpeaker, chosen, StringComparison.Ordinal) ? unit : unit with { ChosenSpeaker = chosen };
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

            return unit.Status == TranslationStatus.Approved ? unit : unit with { Status = TranslationStatus.Approved, Doubt = null };
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

            var restored = unit with { Translation = previous.Translation, Status = previous.Status, Origin = previous.Origin, Doubt = previous.Doubt };
            return restored == unit ? unit : restored;
        });
    }

    /// <summary>
    /// A troca da pontuação do projeto, lida do <c>vnforge.json</c> só quando o arquivo muda (data e tamanho): gravar uma linha não relê nem reinterpreta a configuração.
    /// Config inválida ou estratégia sem encoder falham aqui, com a mensagem de sempre, em vez de gravar sem a troca.
    /// </summary>
    private KeyboardPunctuation? PunctuationOption()
    {
        var file = new FileInfo(project.ConfigPath);
        if (!file.Exists)
        {
            return KeyboardPunctuation.For(project);
        }

        var (stamp, length) = (file.LastWriteTimeUtc, file.Length);
        lock (punctuationGate)
        {
            if (punctuation is { } cached && cached.Stamp == stamp && cached.Length == length)
            {
                return cached.Value;
            }

            var value = KeyboardPunctuation.For(project);
            punctuation = (stamp, length, value);
            return value;
        }
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
