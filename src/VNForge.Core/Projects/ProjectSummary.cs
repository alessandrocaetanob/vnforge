namespace VNForge.Core.Projects;

/// <summary>O que uma tela precisa para mostrar um projeto: dados do <c>vnforge.json</c> e o progresso de <c>text/*.json</c>.</summary>
public sealed record ProjectSummary(
    string Name,
    string Root,
    string EngineId,
    int? EngineVersion,
    string EncodingStrategy,
    string Model,
    int Scripts,
    StatusCounts Counts);

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
            status.Total);
    }
}
