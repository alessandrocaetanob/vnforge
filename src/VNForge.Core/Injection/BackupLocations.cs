using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.Core.Injection;

/// <summary>
/// Onde ficam os backups de um projeto: a pasta que a pessoa escolheu nos Ajustes (com uma subpasta por projeto) ou, sem escolha, <c>backup/</c> dentro do projeto.
/// Trocar de pasta não move nada: as pastas anteriores continuam sendo lidas, e o <c>backup/</c> do projeto também, porque é onde estão os backups feitos antes da escolha.
/// </summary>
/// <param name="Folder">A pasta escolhida; nula para o padrão, <c>backup/</c> do projeto.</param>
/// <param name="PreviousFolders">As pastas escolhidas antes de <paramref name="Folder"/>, da mais recente para a mais antiga.</param>
public sealed record BackupLocations(string? Folder = null, IReadOnlyList<string>? PreviousFolders = null)
{
    public static BackupLocations Default { get; } = new();

    public bool IsCustom => !string.IsNullOrWhiteSpace(Folder);

    /// <summary>Onde o próximo backup do projeto é criado.</summary>
    public string CurrentRoot(ProjectFolder project) => RootIn(Folder, project);

    /// <summary>
    /// Todas as pastas onde pode haver backup do projeto, a atual primeiro. Com uma pasta escolhida, o <c>backup/</c> do projeto vem logo depois,
    /// porque os backups feitos antes da escolha estão lá.
    /// </summary>
    public IReadOnlyList<string> Roots(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var roots = new List<string> { CurrentRoot(project) };
        if (IsCustom)
        {
            roots.Add(project.BackupDirectory);
        }

        foreach (var previous in PreviousFolders ?? [])
        {
            roots.Add(RootIn(previous, project));
        }

        return [.. roots.DistinctBy(r => GameLibrary.NormalizeFolder(r), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)];
    }

    /// <summary>
    /// As pastas escolhidas (a atual e as anteriores) que não existem agora: o disco pode estar desconectado, e os backups que ficaram nelas não aparecem até voltar.
    /// A subpasta do projeto que ainda não foi criada não conta; só a pasta dos Ajustes.
    /// </summary>
    public IReadOnlyList<string> Unreachable(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var missing = new List<string>();
        foreach (var folder in new[] { Folder }.Concat(PreviousFolders ?? []))
        {
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            {
                missing.Add(RootIn(folder, project));
            }
        }

        return missing;
    }

    /// <summary>
    /// As pastas dos Ajustes (a atual e as anteriores), sem a subpasta do projeto: o que o backup à mão não pode copiar para dentro de si mesmo
    /// quando a pessoa escolhe uma pasta dentro de um projeto.
    /// </summary>
    public IReadOnlyList<string> ConfiguredFolders() =>
        [.. new[] { Folder }.Concat(PreviousFolders ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!)];

    /// <summary>
    /// As subpastas só com o nome do projeto, de antes de o código do caminho entrar no nome. Dois projetos com a mesma pasta
    /// dividiam essa subpasta; quem lê só leva dali o que o manifesto diz ser deste projeto.
    /// </summary>
    internal IReadOnlyList<string> LegacyRoots(ProjectFolder project) =>
        [.. ConfiguredFolders().Select(f => Path.Combine(f, FolderName(project))).Except(Roots(project), PathComparer)];

    /// <summary>
    /// O nome da subpasta do projeto dentro da pasta dos backups: o nome da pasta do projeto e oito dígitos do SHA-256 do caminho dela,
    /// para dois projetos com a mesma pasta (<c>traducao</c>) em lugares diferentes não dividirem os backups.
    /// </summary>
    public static string ProjectSubfolder(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = GameLibrary.NormalizeFolder(project.Root);
        var key = OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;
        var code = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..8];
        return $"{FolderName(project)}-{code}";
    }

    private static string FolderName(ProjectFolder project)
    {
        var name = Path.GetFileName(project.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "projeto" : name;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string RootIn(string? folder, ProjectFolder project) =>
        string.IsNullOrWhiteSpace(folder) ? project.BackupDirectory : Path.Combine(folder, ProjectSubfolder(project));
}
