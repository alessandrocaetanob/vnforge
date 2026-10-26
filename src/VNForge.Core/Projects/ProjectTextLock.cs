using System.Collections.Concurrent;

namespace VNForge.Core.Projects;

/// <summary>
/// A trava que serializa quem lê e regrava os <c>text/*.json</c> de um projeto dentro do processo: o <see cref="LineEditor"/> e as execuções
/// de IA. Sem ela, duas leituras seguidas de gravações perdem a edição da que gravou primeiro. A chave é a pasta do projeto, não a instância
/// de <see cref="ProjectFolder"/>, porque cada tela abre a sua.
/// </summary>
public static class ProjectTextLock
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static object For(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Root));
        return Locks.GetOrAdd(key, static _ => new object());
    }
}
