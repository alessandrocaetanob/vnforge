using System.Runtime.ExceptionServices;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// Uma única tradução por nome do jogo. O personagem de <c>characters.json</c> e as unidades <see cref="TextUnitKind.Name"/> com o mesmo original
/// guardam a mesma tradução: <see cref="ApplyToUnits"/> leva a do personagem para as unidades, e o <see cref="LineEditor"/> leva a de uma unidade
/// editada para o personagem e para as outras unidades.
/// </summary>
public static class CharacterNameSync
{
    /// <summary>
    /// Grava a tradução de cada personagem com <see cref="CharacterEntry.Target"/> nas unidades <c>name</c> do mesmo original (achadas por
    /// <see cref="CharacterBook.Find"/>), como o <see cref="LineEditor"/> grava uma edição manual. Unidades "não traduzir" e as que já têm o texto ficam como estão.
    /// </summary>
    /// <param name="scriptKeys">Os scripts a ler; nulo lê todos os <c>text/*.json</c>. A página do Glossário passa só os scripts que têm nomes.</param>
    /// <returns>Quantas unidades mudaram.</returns>
    /// <exception cref="TextFileException">Um script está ausente ou inválido.</exception>
    /// <exception cref="IOException">Um script não pôde ser gravado; os anteriores já estão no disco.</exception>
    public static int ApplyToUnits(ProjectFolder project, LineEditor editor, IEnumerable<CharacterEntry> characters, IEnumerable<string>? scriptKeys = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(characters);

        var book = new CharacterBook(characters.Where(c => !string.IsNullOrWhiteSpace(c.Target)));
        var scripts = scriptKeys is null
            ? project.LoadAllText()
            : scriptKeys.Distinct(StringComparer.Ordinal).ToDictionary(key => key, project.LoadText, StringComparer.Ordinal);

        var byTarget = new Dictionary<string, List<(string ScriptKey, string Id)>>(StringComparer.Ordinal);
        foreach (var (scriptKey, units) in scripts)
        {
            foreach (var unit in units.Where(u => u.Kind == TextUnitKind.Name && u.Status != TranslationStatus.Keep))
            {
                var target = book.Find(unit.Source)?.Target.Trim();
                if (target is null || string.Equals(unit.Translation, target, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!byTarget.TryGetValue(target, out var list))
                {
                    byTarget[target] = list = [];
                }

                list.Add((scriptKey, unit.Id));
            }
        }

        var changed = 0;
        foreach (var (target, lines) in byTarget)
        {
            var result = editor.SetTranslationFor(lines, target);
            changed += result.Changes.Count;
            if (result.Failure is not null)
            {
                ExceptionDispatchInfo.Capture(result.Failure).Throw();
            }
        }

        return changed;
    }
}
