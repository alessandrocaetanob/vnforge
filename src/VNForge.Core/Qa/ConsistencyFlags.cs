using VNForge.Core.Projects;

namespace VNForge.Core.Qa;

/// <summary>
/// "Manter diferentes de propósito": grava em <see cref="Text.TextUnit.Flags"/> de cada linha do grupo a marca <see cref="ConsistencyGroups.FlagFor"/>,
/// que leva a impressão do grupo (<see cref="ConsistencyGroup.Fingerprint"/>). A regra <see cref="InconsistentTranslationRule"/> para de avisar enquanto
/// toda linha do grupo tiver a marca de agora; mudar o original ou uma tradução muda a impressão e o grupo volta sozinho. A marca fica no
/// <c>text/&lt;chave&gt;.json</c>, então sobrevive a fechar e abrir o projeto e à re-extração.
/// </summary>
public static class ConsistencyFlags
{
    /// <summary>Mantém o <paramref name="group"/> como está: as traduções diferentes são de propósito.</summary>
    /// <returns>Quantas linhas foram regravadas.</returns>
    /// <exception cref="VNForgeException">O grupo mudou no disco depois de conferido, ou um script não abre.</exception>
    public static int Keep(ProjectFolder project, ConsistencyGroup group)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(group);
        lock (ProjectTextLock.For(project))
        {
            var current = ConsistencyGroups.Find(QaContext.ForTexts(project.LoadAllText()))
                .FirstOrDefault(g => string.Equals(g.Source, group.Source, StringComparison.Ordinal));
            if (current is null || !string.Equals(current.Fingerprint, group.Fingerprint, StringComparison.Ordinal))
            {
                throw new VNForgeException("As traduções deste original mudaram depois da conferência. Confira de novo.");
            }

            var flag = ConsistencyGroups.FlagFor(current);
            var written = 0;
            foreach (var script in current.Lines.GroupBy(l => l.ScriptKey, StringComparer.Ordinal))
            {
                var wanted = script.Select(l => l.UnitId).ToHashSet(StringComparer.Ordinal);
                var units = project.LoadText(script.Key).ToList();
                var changed = 0;
                for (var i = 0; i < units.Count; i++)
                {
                    if (!wanted.Contains(units[i].Id))
                    {
                        continue;
                    }

                    // A marca velha de um grupo que mudou sai; só a de agora fica.
                    IReadOnlyList<string> flags = [.. units[i].Flags.Where(f => !f.StartsWith(ConsistencyGroups.FlagPrefix, StringComparison.Ordinal)), flag];
                    if (flags.SequenceEqual(units[i].Flags, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    units[i] = units[i] with { Flags = flags };
                    changed++;
                }

                if (changed > 0)
                {
                    project.SaveText(script.Key, units);
                    written += changed;
                }
            }

            return written;
        }
    }
}
