using System.Text;
using System.Text.Json;

namespace VNForge.Core.Projects;

/// <summary>Uma linha aprovada: em que script, qual linha e quando.</summary>
public sealed record ApprovalRecord(string ScriptKey, string UnitId, DateTimeOffset At);

/// <summary>
/// O registro de aprovações em <c>.cache/sessions.json</c>: cada vez que uma pessoa aprova uma linha, a hora fica guardada. É o que diz quantas linhas
/// foram revisadas hoje (o modo revisão e a barra de status) e, mais adiante, o ritmo de revisão. Quem escreve é o app; apagar o <c>.cache/</c>
/// só zera o registro, nenhuma tradução se perde. As aprovações mais antigas que <see cref="Retention"/> saem na gravação, para o arquivo não crescer sem fim.
/// </summary>
public sealed record SessionsFile(int Version, IReadOnlyList<ApprovalRecord> Approvals)
{
    public const int CurrentVersion = 1;

    /// <summary>Quanto tempo uma aprovação fica no registro; o ritmo usa os últimos sete dias, e o resto é folga.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private const string FileName = "sessions.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SessionsFile Empty { get; } = new(CurrentVersion, []);

    public static string PathFor(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.CacheDirectory, FileName);
    }

    /// <summary>O registro gravado, ou <see cref="Empty"/> quando não há arquivo ou ele não é legível: a revisão não trava por causa dele.</summary>
    public static SessionsFile Load(ProjectFolder project)
    {
        try
        {
            var path = PathFor(project);
            var read = File.Exists(path) ? JsonSerializer.Deserialize<SessionsFile>(Encoding.UTF8.GetString(ProjectFileIo.ReadAllBytes(path)), Json) : null;
            return read is { Version: CurrentVersion, Approvals: not null } ? read : Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>Grava o registro de forma atômica (arquivo temporário e troca).</summary>
    public static void Save(ProjectFolder project, SessionsFile sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var path = PathFor(project);
        Directory.CreateDirectory(project.CacheDirectory);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(sessions with { Version = CurrentVersion }, Json) + "\n", new UTF8Encoding(false));
            ProjectFileIo.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// O registro com mais uma aprovação, sem as que passaram de <see cref="Retention"/> em relação a <paramref name="approval"/>.
    /// Aprovar de novo a mesma linha no mesmo dia não duplica a contagem de <see cref="ReviewedOn"/>.
    /// </summary>
    public SessionsFile With(ApprovalRecord approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var oldest = approval.At - Retention;
        return this with { Approvals = [.. Approvals.Where(a => a.At >= oldest), approval] };
    }

    /// <summary>
    /// Quantas linhas diferentes foram aprovadas no dia de <paramref name="now"/> no fuso <paramref name="zone"/>. Uma linha aprovada duas vezes no mesmo dia
    /// (desfeita e aprovada de novo) conta uma vez.
    /// </summary>
    public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return Approvals
            .Where(a => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.At, zone).DateTime) == day)
            .Select(a => (a.ScriptKey, a.UnitId))
            .Distinct()
            .Count();
    }

    /// <summary>
    /// Lê, acrescenta a aprovação e regrava. Uma falha de disco não derruba quem aprova: a tradução já foi gravada e o registro só alimenta o contador.
    /// </summary>
    /// <returns>Verdadeiro quando a aprovação ficou gravada no arquivo.</returns>
    public static bool TryRecord(ProjectFolder project, ApprovalRecord approval)
    {
        try
        {
            Save(project, Load(project).With(approval));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
