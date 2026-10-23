using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Review;

/// <summary>Resultado de <see cref="ReviewImporter.Import"/>.</summary>
/// <param name="Rows">Linhas de dados do arquivo.</param>
/// <param name="ChangedUnits">Quantas unidades tiveram tradução, status ou notas trocados.</param>
/// <param name="ChangedScripts">Chaves dos scripts cujo <c>text/&lt;chave&gt;.json</c> foi regravado, em ordem.</param>
public sealed record ReviewImportResult(int Rows, int ChangedUnits, IReadOnlyList<string> ChangedScripts);

/// <summary>
/// Aplica um arquivo de revisão aos <c>text/*.json</c>, casando por id. Só a tradução, o status e as notas mudam; origem, falante, original,
/// avisos e locator ficam como estão. O arquivo inteiro é conferido antes de qualquer gravação, então um erro deixa o projeto intacto,
/// e só se regrava o script que teve alguma linha alterada.
/// </summary>
public static class ReviewImporter
{
    /// <param name="project">Projeto de destino.</param>
    /// <param name="path">Arquivo de revisão.</param>
    /// <param name="format">Formato do arquivo; quando nulo, vem da extensão.</param>
    /// <exception cref="ReviewImportException">Arquivo ausente, de formato desconhecido ou inválido, id desconhecido ou repetido, status inválido.</exception>
    /// <exception cref="TextFileException">Algum <c>text/*.json</c> é inválido.</exception>
    public static ReviewImportResult Import(ProjectFolder project, string path, IReviewFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ReviewImportException($"Arquivo de revisão '{fullPath}' não encontrado.");
        }

        format ??= ReviewFormats.FromPath(fullPath)
            ?? throw new ReviewImportException($"Não reconheço o formato de '{fullPath}'; use --format ({string.Join(", ", ReviewFormats.All.Select(f => f.Name))}).");

        var rows = format.Read(File.ReadAllBytes(fullPath), fullPath);

        var scripts = project.LoadAllText().ToDictionary(s => s.Key, s => s.Value.ToList());
        var changes = Collect(rows, scripts);
        foreach (var (key, index, unit) in changes)
        {
            scripts[key][index] = unit;
        }

        var changedScripts = changes.Select(c => c.Key).Distinct().Order(StringComparer.Ordinal).ToList();
        SaveAll(project, scripts, changedScripts);
        return new ReviewImportResult(rows.Count, changes.Count, changedScripts);
    }

    private static Dictionary<string, (string Key, int Index)> IndexById(Dictionary<string, List<TextUnit>> scripts)
    {
        var index = new Dictionary<string, (string Key, int Index)>(StringComparer.Ordinal);
        foreach (var (key, units) in scripts)
        {
            for (var i = 0; i < units.Count; i++)
            {
                index[units[i].Id] = (key, i);
            }
        }

        return index;
    }

    /// <summary>Confere todas as linhas e devolve as unidades que mudam; qualquer erro vira uma só <see cref="ReviewImportException"/>.</summary>
    private static List<(string Key, int Index, TextUnit Unit)> Collect(IReadOnlyList<ReviewRow> rows, Dictionary<string, List<TextUnit>> scripts)
    {
        var where = IndexById(scripts);
        var errors = new List<ReviewImportError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<(string Key, int Index, TextUnit Unit)>();
        foreach (var row in rows)
        {
            var error = Check(row, seen, where, out var status, out var at);
            if (error is not null)
            {
                errors.Add(new ReviewImportError(row.Line, error));
                continue;
            }

            var current = scripts[at.Key][at.Index];
            if (!string.Equals(current.Translation ?? string.Empty, row.Translation, StringComparison.Ordinal)
                || current.Status != status
                || !string.Equals(current.Notes, row.Notes, StringComparison.Ordinal))
            {
                changes.Add((at.Key, at.Index, current with
                {
                    Translation = row.Translation.Length == 0 ? null : row.Translation,
                    Status = status,
                    Notes = row.Notes,
                }));
            }
        }

        return errors.Count > 0 ? throw new ReviewImportException(errors) : changes;
    }

    private static string? Check(
        ReviewRow row,
        HashSet<string> seen,
        Dictionary<string, (string Key, int Index)> where,
        out TranslationStatus status,
        out (string Key, int Index) at)
    {
        status = default;
        at = default;
        if (!seen.Add(row.Id))
        {
            return $"o id '{row.Id}' está repetido no arquivo.";
        }

        if (!where.TryGetValue(row.Id, out at))
        {
            return $"id desconhecido '{row.Id}': não existe no projeto.";
        }

        return TryParseStatus(row.Status, out status)
            ? null
            : $"status inválido '{row.Status}' em '{row.Id}'; use {string.Join(", ", Enum.GetValues<TranslationStatus>().Select(ReviewExporter.EnumName))}.";
    }

    private static void SaveAll(ProjectFolder project, Dictionary<string, List<TextUnit>> scripts, List<string> keys)
    {
        var originals = keys.ToDictionary(k => k, k => File.ReadAllBytes(project.TextPath(k)));
        var written = new List<string>();
        try
        {
            foreach (var key in keys)
            {
                project.SaveText(key, scripts[key]);
                written.Add(key);
            }
        }
        catch
        {
            // Falha de IO no meio: devolve os scripts já gravados para não deixar a importação pela metade.
            foreach (var key in written)
            {
                File.WriteAllBytes(project.TextPath(key), originals[key]);
            }

            throw;
        }
    }

    private static bool TryParseStatus(string text, out TranslationStatus status)
    {
        try
        {
            status = JsonSerializer.Deserialize<TranslationStatus>($"\"{text.Trim().ToLowerInvariant()}\"", TextUnitJson.Options);
            return true;
        }
        catch (JsonException)
        {
            status = default;
            return false;
        }
    }
}
