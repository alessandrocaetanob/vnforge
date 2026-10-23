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
        var where = new Dictionary<string, (string Key, int Index)>(StringComparer.Ordinal);
        foreach (var (key, units) in scripts)
        {
            for (var i = 0; i < units.Count; i++)
            {
                where[units[i].Id] = (key, i);
            }
        }

        var errors = new List<ReviewImportError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<(string Key, int Index, TextUnit Unit)>();
        foreach (var row in rows)
        {
            if (!seen.Add(row.Id))
            {
                errors.Add(new ReviewImportError(row.Line, $"o id '{row.Id}' está repetido no arquivo."));
                continue;
            }

            if (!where.TryGetValue(row.Id, out var at))
            {
                errors.Add(new ReviewImportError(row.Line, $"id desconhecido '{row.Id}': não existe no projeto."));
                continue;
            }

            if (!TryParseStatus(row.Status, out var status))
            {
                errors.Add(new ReviewImportError(row.Line, $"status inválido '{row.Status}' em '{row.Id}'; use {string.Join(", ", Enum.GetValues<TranslationStatus>().Select(ReviewExporter.EnumName))}."));
                continue;
            }

            var current = scripts[at.Key][at.Index];
            if (string.Equals(current.Translation ?? string.Empty, row.Translation, StringComparison.Ordinal)
                && current.Status == status
                && string.Equals(current.Notes, row.Notes, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add((at.Key, at.Index, current with
            {
                Translation = row.Translation.Length == 0 ? null : row.Translation,
                Status = status,
                Notes = row.Notes,
            }));
        }

        if (errors.Count > 0)
        {
            throw new ReviewImportException(errors);
        }

        foreach (var (key, index, unit) in changes)
        {
            scripts[key][index] = unit;
        }

        var changedScripts = changes.Select(c => c.Key).Distinct().Order(StringComparer.Ordinal).ToList();
        var originals = changedScripts.ToDictionary(k => k, k => File.ReadAllBytes(project.TextPath(k)));
        var written = new List<string>();
        try
        {
            foreach (var key in changedScripts)
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

        return new ReviewImportResult(rows.Count, changes.Count, changedScripts);
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
