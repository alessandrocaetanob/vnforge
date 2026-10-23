using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// "Marcar como ok": grava o id de uma regra em <see cref="TextUnit.Flags"/> da linha, e o <see cref="QaEngine"/> passa a silenciar aquele
/// aviso ali. A marca fica no <c>text/&lt;chave&gt;.json</c> e sobrevive à re-extração (E3.5 mantém <c>flags</c> quando o original não mudou).
/// </summary>
public static class QaFlags
{
    /// <summary>Marca o aviso da regra <paramref name="ruleId"/> como ok na linha <paramref name="unitId"/>.</summary>
    /// <returns>Verdadeiro se gravou; falso se a linha já tinha a marca (nada é regravado).</returns>
    /// <exception cref="VNForgeException">Regra desconhecida em <paramref name="engine"/> ou linha inexistente.</exception>
    public static bool MarkOk(ProjectFolder project, QaEngine engine, string unitId, string ruleId) =>
        Update(project, engine, unitId, ruleId, add: true);

    /// <summary>Tira a marca de ok da linha, para o aviso voltar a aparecer.</summary>
    /// <returns>Verdadeiro se a marca existia e saiu.</returns>
    /// <exception cref="VNForgeException">Regra desconhecida em <paramref name="engine"/> ou linha inexistente.</exception>
    public static bool Unmark(ProjectFolder project, QaEngine engine, string unitId, string ruleId) =>
        Update(project, engine, unitId, ruleId, add: false);

    private static bool Update(ProjectFolder project, QaEngine engine, string unitId, string ruleId, bool add)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrEmpty(unitId);
        ArgumentException.ThrowIfNullOrEmpty(ruleId);

        if (engine.Find(ruleId) is null)
        {
            throw new VNForgeException($"Regra de QA desconhecida: {ruleId}.");
        }

        var separator = unitId.IndexOf(':', StringComparison.Ordinal);
        var key = separator > 0 ? unitId[..separator] : unitId;
        string path;
        try
        {
            path = project.TextPath(key);
        }
        catch (ArgumentException)
        {
            throw new VNForgeException($"Id de linha inválido: '{unitId}'.");
        }

        if (!File.Exists(path))
        {
            throw new VNForgeException($"Linha {unitId} não encontrada: não existe text/{key}.json.");
        }

        var units = project.LoadText(key);
        var index = -1;
        for (var i = 0; i < units.Count; i++)
        {
            if (string.Equals(units[i].Id, unitId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new VNForgeException($"Linha {unitId} não encontrada em text/{key}.json.");
        }

        var flags = units[index].Flags;
        if (flags.Contains(ruleId, StringComparer.Ordinal) == add)
        {
            return false;
        }

        IReadOnlyList<string> updated = add ? [.. flags, ruleId] : [.. flags.Where(f => !string.Equals(f, ruleId, StringComparison.Ordinal))];
        var edited = units.ToList();
        edited[index] = units[index] with { Flags = updated };
        project.SaveText(key, edited);
        return true;
    }
}
