using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// O ponto de restauração (#478) de uma operação em lote, sem a lista de tarefas do app: guarda o estado dos scripts antes e fecha o ponto depois.
/// O app e o <c>vnf</c> passam por aqui. O ponto é um extra: se não puder ser guardado, a operação roda do mesmo jeito, sem "Desfazer".
/// </summary>
public static class RestoreScope
{
    /// <summary>Guarda o estado de <paramref name="scriptKeys"/> (nulo guarda todos); <see langword="null"/> quando o disco não deixou.</summary>
    /// <param name="include">Decide, linha a linha (antes, depois), se a mudança é da operação; nulo aceita toda linha que mudou.</param>
    public static RestorePointRecorder? TryBegin(
        ProjectFolder project, string title, IEnumerable<string>? scriptKeys = null, Func<TextUnit, TextUnit, bool>? include = null, TimeProvider? clock = null, bool trackFormatting = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        try
        {
            return new RestorePointStore(project, clock).Begin(title, scriptKeys, include, trackFormatting);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Fecha a captura de <see cref="TryBegin"/>; <see langword="null"/> quando a operação não mudou linha, ou quando o disco não deixou guardar o ponto.</summary>
    /// <param name="only">As linhas que a operação diz ter gravado; sem isso o ponto guarda toda linha que mudou nos scripts.</param>
    public static RestorePoint? TryComplete(RestorePointRecorder? recorder, IEnumerable<(string ScriptKey, string UnitId)>? only = null)
    {
        if (recorder is null)
        {
            return null;
        }

        try
        {
            return recorder.Complete(only);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
