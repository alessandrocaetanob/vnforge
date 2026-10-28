namespace VNForge.Core.Projects;

/// <summary>O que uma tela precisa para mostrar um projeto: dados do <c>vnforge.json</c> e o progresso de <c>text/*.json</c>.</summary>
/// <param name="IsSample">É a cena de exemplo da primeira vez: não tem jogo, então o build e a injeção não se aplicam.</param>
public sealed record ProjectSummary(
    string Name,
    string Root,
    string EngineId,
    int? EngineVersion,
    string EncodingStrategy,
    string Model,
    int Scripts,
    StatusCounts Counts,
    LastBuild? LastBuild = null,
    string? LastEditedScript = null,
    string? Effort = null,
    bool IsSample = false);

/// <summary>O que a pasta <c>build/</c> de um projeto guarda do último <c>vnf build</c>.</summary>
/// <param name="BuiltAt">Quando o arquivo mais novo de <c>build/</c> foi gravado.</param>
/// <param name="Scripts">Scripts que o build trocou nos pacotes; nulo quando o <c>.cache/build-state.json</c> falta ou não confere com o <c>build/</c>.</param>
public sealed record LastBuild(DateTimeOffset BuiltAt, int? Scripts);

/// <summary>Lê o resumo de uma pasta de projeto. Só lê; não grava nada.</summary>
public static class ProjectSummaryReader
{
    /// <exception cref="ProjectConfigException"><c>vnforge.json</c> ausente ou inválido.</exception>
    /// <exception cref="VNForge.Core.Text.TextFileException">Algum <c>text/*.json</c> inválido.</exception>
    public static ProjectSummary Read(string root)
    {
        var folder = ProjectFolder.Open(root);
        var config = folder.LoadConfig();
        var status = ProjectStatusReporter.Create(folder);
        return new ProjectSummary(
            config.Name,
            folder.Root,
            config.Engine.Id,
            config.Engine.Version,
            config.Encoding.Strategy,
            config.Ai.Model,
            status.Files.Count,
            status.Total,
            ReadLastBuild(folder),
            FindLastEditedScript(folder, status),
            config.Ai.Effort,
            Demo.SampleProject.IsSample(folder.Root));
    }

    /// <summary>Data e scripts do último build, lidos de <c>build/</c> e do estado do build; nulo quando <c>build/</c> não tem arquivo.</summary>
    private static LastBuild? ReadLastBuild(ProjectFolder folder)
    {
        try
        {
            if (!Directory.Exists(folder.BuildDirectory))
            {
                return null;
            }

            var files = new DirectoryInfo(folder.BuildDirectory).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            if (files.Count == 0)
            {
                return null;
            }

            var builtAt = new DateTimeOffset(files.Max(f => f.LastWriteTimeUtc), TimeSpan.Zero);
            var scripts = BuildState.Load(folder) is { } state && state.ArchivesIntact(folder)
                ? state.Archives.Sum(a => a.ReplacedFiles)
                : (int?)null;
            return new LastBuild(builtAt, scripts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // O build é só informação do Início: uma pasta que não se lê vale como "sem build".
            return null;
        }
    }

    /// <summary>
    /// O script em que continuar: o de <c>text/*.json</c> gravado por último entre os que já têm alguma linha traduzida; sem tradução nenhuma,
    /// o primeiro script. A extração grava todos os arquivos de uma vez, então só um arquivo com tradução diz que alguém mexeu nele.
    /// </summary>
    private static string? FindLastEditedScript(ProjectFolder folder, ProjectStatus status)
    {
        var worked = status.Files.Where(f => f.Counts.Done > 0).ToList();
        if (worked.Count == 0)
        {
            return status.Files.FirstOrDefault()?.Key;
        }

        // GetLastWriteTimeUtc não lança: um arquivo que sumiu vale como o mais antigo.
        return worked
            .OrderByDescending(f => File.GetLastWriteTimeUtc(folder.TextPath(f.Key)))
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .First().Key;
    }
}
