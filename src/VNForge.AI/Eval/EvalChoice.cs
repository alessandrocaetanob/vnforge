using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.AI.Eval;

/// <summary>
/// Aplica ao projeto a configuração que a pessoa escolheu para o M4: o modelo, o esforço e a janela de contexto passam para o <c>ai</c> do <c>vnforge.json</c>, de onde
/// <c>vnf translate</c> e o app os leem. A bíblia não tem campo no projeto ainda: ela entra com o c2.
/// </summary>
public static class EvalChoice
{
    public static void Apply(ProjectFolder project, EvalConfig config)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(config);
        var current = project.LoadConfig();
        project.SaveConfig(current with
        {
            Ai = current.Ai with
            {
                Model = config.Model,
                Effort = config.Effort,
                ContextBefore = config.ContextLines,
                ContextAfter = config.ContextLines,
            },
        });
    }
}
