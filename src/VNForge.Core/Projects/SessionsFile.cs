using System.Text;
using System.Text.Json;

namespace VNForge.Core.Projects;

/// <summary>Uma linha aprovada: em que script, qual linha e quando.</summary>
public sealed record ApprovalRecord(string ScriptKey, string UnitId, DateTimeOffset At);

/// <summary>
/// O registro de aprovações em <c>.cache/sessions.json</c>: cada vez que uma pessoa aprova uma linha, a hora fica guardada. É o que diz quantas linhas
/// foram revisadas hoje (o modo revisão e a barra de status) e, mais adiante, o ritmo de revisão. Quem escreve, por enquanto, é o app (a CLI ainda não grava aqui); apagar o <c>.cache/</c>
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
    public static SessionsFile Load(ProjectFolder project) => Read(project).File;

    /// <summary>
    /// Lê o registro dizendo também o que aconteceu: quem grava só pode trocar o arquivo quando <see cref="SessionsLoad.CanOverwrite"/>, para um arquivo
    /// que não abriu agora (disco ocupado) ou que é de uma versão mais nova não perder o histórico para um registro vazio.
    /// </summary>
    public static SessionsLoad Read(ProjectFolder project)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(ProjectFileIo.ReadAllBytes(PathFor(project)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SessionsLoad(Empty, SessionsLoadStatus.Missing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SessionsLoad(Empty, SessionsLoadStatus.Unreadable);
        }

        return Parse(text);
    }

    private static SessionsLoad Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("version", out var version) && version.TryGetInt32(out var number) && number > CurrentVersion)
            {
                return new SessionsLoad(Empty, SessionsLoadStatus.NewerVersion);
            }

            var read = root.Deserialize<SessionsFile>(Json);
            return read is { Version: CurrentVersion, Approvals: not null }
                ? new SessionsLoad(read with { Approvals = [.. read.Approvals.Where(a => a is not null)] }, SessionsLoadStatus.Ok)
                : new SessionsLoad(Empty, SessionsLoadStatus.Corrupt);
        }
        catch (JsonException)
        {
            return new SessionsLoad(Empty, SessionsLoadStatus.Corrupt);
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

    /// <summary>O registro sem a última aprovação da linha, quando a pessoa desfaz a aprovação; sem ela, devolve o próprio registro.</summary>
    public SessionsFile Without(string scriptKey, string unitId)
    {
        var index = -1;
        for (var i = 0; i < Approvals.Count; i++)
        {
            if (Approvals[i].ScriptKey == scriptKey && Approvals[i].UnitId == unitId)
            {
                index = i;
            }
        }

        return index < 0 ? this : this with { Approvals = [.. Approvals.Where((_, i) => i != index)] };
    }
}

/// <summary>O que a leitura de <c>sessions.json</c> encontrou.</summary>
public enum SessionsLoadStatus
{
    /// <summary>O arquivo abriu e é da versão que este programa conhece.</summary>
    Ok,

    /// <summary>Não há arquivo: o registro começa vazio.</summary>
    Missing,

    /// <summary>O arquivo não é JSON válido ou não tem o formato esperado: não há histórico a preservar.</summary>
    Corrupt,

    /// <summary>O arquivo existe mas não abriu (disco ocupado, acesso negado): tem histórico que esta leitura não viu.</summary>
    Unreadable,

    /// <summary>O arquivo é de uma versão mais nova que este programa entende: tem histórico que ele não sabe ler.</summary>
    NewerVersion,
}

/// <summary>O registro lido e como a leitura terminou. <see cref="CanOverwrite"/> diz se gravar por cima do arquivo não perde histórico.</summary>
public sealed record SessionsLoad(SessionsFile File, SessionsLoadStatus Status)
{
    public bool CanOverwrite => Status is SessionsLoadStatus.Ok or SessionsLoadStatus.Missing or SessionsLoadStatus.Corrupt;
}
