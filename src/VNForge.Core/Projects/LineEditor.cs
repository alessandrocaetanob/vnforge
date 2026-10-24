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

    /// <summary>Devolve a linha ao estado que ela tinha (tradução, status, origem e notas), p. ex. para desfazer uma gravação.</summary>
    /// <exception cref="KeyNotFoundException">O script não tem uma linha com o id de <paramref name="previous"/>.</exception>
    public TextUnit Restore(string scriptKey, TextUnit previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return Update(scriptKey, previous.Id, unit => unit == previous ? unit : previous);
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
