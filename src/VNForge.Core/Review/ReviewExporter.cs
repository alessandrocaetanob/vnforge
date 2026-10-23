using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Review;

/// <summary>Exporta as linhas de <c>text/*.json</c> para um arquivo de revisão. Só lê o projeto.</summary>
public static class ReviewExporter
{
    /// <summary>Grava todas as linhas do projeto (scripts por chave, linhas na ordem do arquivo) em <paramref name="path"/>, trocando o arquivo se já existir.</summary>
    /// <returns>Quantas linhas foram exportadas.</returns>
    /// <exception cref="TextFileException">Algum <c>text/*.json</c> é inválido; nada é gravado.</exception>
    public static int Export(ProjectFolder project, IReviewFormat format, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var rows = project.LoadAllText().Values
            .SelectMany(units => units)
            .Select(ToRow)
            .ToList();
        var data = format.Write(rows);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }

        return rows.Count;
    }

    private static ReviewRow ToRow(TextUnit unit) => new(
        0,
        unit.Id,
        EnumName(unit.Kind),
        unit.Speaker ?? string.Empty,
        unit.Source,
        unit.Translation ?? string.Empty,
        EnumName(unit.Status),
        unit.Notes);

    /// <summary>Nome que o enum tem no JSON (<c>dialogue</c>, <c>machine</c>...).</summary>
    internal static string EnumName<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value, TextUnitJson.Options).Trim('"');
}
