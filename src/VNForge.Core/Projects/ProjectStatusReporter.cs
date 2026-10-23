using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Linhas por <see cref="TranslationStatus"/>. <see cref="Done"/> é tudo que já saiu de <see cref="TranslationStatus.Untranslated"/>.</summary>
public sealed record StatusCounts(int Untranslated, int Machine, int Translated, int Reviewed, int Approved)
{
    public int Total => Untranslated + Machine + Translated + Reviewed + Approved;

    public int Done => Total - Untranslated;

    /// <summary>Percentual de <see cref="Done"/> sobre <see cref="Total"/>, de 0 a 100, truncado em uma casa (só chega a 100 quando nada está pendente); 0 quando não há linhas.</summary>
    public double Percent => Total == 0 ? 0 : Math.Floor(1000.0 * Done / Total) / 10;

    public static StatusCounts Of(IEnumerable<TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        var counts = new int[Enum.GetValues<TranslationStatus>().Length];
        foreach (var unit in units)
        {
            counts[(int)unit.Status]++;
        }

        return new StatusCounts(
            counts[(int)TranslationStatus.Untranslated],
            counts[(int)TranslationStatus.Machine],
            counts[(int)TranslationStatus.Translated],
            counts[(int)TranslationStatus.Reviewed],
            counts[(int)TranslationStatus.Approved]);
    }
}

/// <summary>Contagem de um script (<c>text/&lt;Key&gt;.json</c>).</summary>
public sealed record FileStatus(string Key, StatusCounts Counts);

/// <summary>Contagem de um falante; <see cref="Speaker"/> nulo reúne as linhas sem falante.</summary>
public sealed record CharacterStatus(string? Speaker, StatusCounts Counts);

/// <summary>Progresso do projeto: total, por arquivo (pela chave) e por personagem (mais linhas primeiro; sem falante por último).</summary>
public sealed record ProjectStatus(StatusCounts Total, IReadOnlyList<FileStatus> Files, IReadOnlyList<CharacterStatus> Characters);

/// <summary>Conta as linhas de <c>text/*.json</c> por status, por arquivo e por personagem. Só lê; não grava nada.</summary>
public static class ProjectStatusReporter
{
    /// <exception cref="TextFileException">Algum <c>text/*.json</c> inválido.</exception>
    public static ProjectStatus Create(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Create(project.LoadAllText());
    }

    public static ProjectStatus Create(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var files = texts
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new FileStatus(t.Key, StatusCounts.Of(t.Value)))
            .ToList();

        var all = texts.Values.SelectMany(units => units).ToList();
        var characters = all
            .GroupBy(u => string.IsNullOrEmpty(u.Speaker) ? null : u.Speaker, StringComparer.Ordinal)
            .Select(g => new CharacterStatus(g.Key, StatusCounts.Of(g)))
            .OrderBy(c => c.Speaker is null)
            .ThenByDescending(c => c.Counts.Total)
            .ThenBy(c => c.Speaker, StringComparer.Ordinal)
            .ToList();

        return new ProjectStatus(StatusCounts.Of(all), files, characters);
    }
}
