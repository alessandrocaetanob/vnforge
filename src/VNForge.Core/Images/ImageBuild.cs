using System.Security.Cryptography;
using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Images;

/// <summary>Resultado de <see cref="ImageBuild.Apply"/>.</summary>
/// <param name="Archives">Pacotes que receberam imagens, com quantas foram trocadas em cada um.</param>
/// <param name="Warnings">Edições que ficaram de fora e por quê (em português).</param>
public sealed record ImageBuildResult(IReadOnlyList<RepackedArchive> Archives, IReadOnlyList<string> Warnings)
{
    public static ImageBuildResult None { get; } = new([], []);
}

/// <summary>
/// A parte do build que devolve ao pacote do jogo as imagens editadas (#535). Para cada pacote com edição marcada, troca só as imagens
/// editadas pela reescrita mínima do <see cref="IArchive.WriteTo"/>: as outras entradas saem byte a byte iguais (regra 3 do <c>CLAUDE.md</c>).
/// </summary>
public static class ImageBuild
{
    /// <summary>
    /// Fingerprint das edições marcadas: muda quando uma edição ou a imagem pronta dela muda; vazio sem edição marcada. O build incremental
    /// compara esse valor para não reaproveitar um <c>build/</c> feito com outras imagens.
    /// </summary>
    public static string Fingerprint(ProjectFolder project)
    {
        var marked = project.LoadImageEdits().Where(e => e.Marked).OrderBy(e => e.Archive, StringComparer.Ordinal).ThenBy(e => e.Path, StringComparer.Ordinal).ToList();
        if (marked.Count == 0)
        {
            return string.Empty;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var edit in marked)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{edit.Archive}\n{edit.Path}\n{edit.Hash}\n"));
            var file = project.EditedImagePath(edit.Archive, edit.Path);
            hash.AppendData(File.Exists(file) ? SHA256.HashData(File.ReadAllBytes(file)) : []);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Grava em <paramref name="outputDirectory"/>, no caminho relativo ao jogo, os pacotes com as imagens editadas do <paramref name="project"/>.</summary>
    /// <param name="project">Projeto com o <c>image-edits.json</c> e a pasta <c>images/</c>.</param>
    /// <param name="gameDirectory">Pasta do jogo, de onde vêm os pacotes originais.</param>
    /// <param name="formats">Formatos de pacote da engine.</param>
    /// <param name="outputDirectory">Destino do build. Um pacote que já está lá (o build dos scripts) é usado como base; senão a base é o original.</param>
    /// <param name="originalOf">Como <see cref="IGameRepacker.Repack"/>: o arquivo de onde ler o pacote original (o do backup, com o jogo injetado).</param>
    /// <param name="cancellationToken">Cancela entre um pacote e outro.</param>
    /// <exception cref="VNForgeException">Falha de leitura ou de gravação de um pacote; a mensagem, em português, cita o pacote.</exception>
    public static ImageBuildResult Apply(
        ProjectFolder project,
        string gameDirectory,
        IReadOnlyList<IArchiveFormat> formats,
        string outputDirectory,
        Func<string, string>? originalOf = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(formats);

        var marked = project.LoadImageEdits().Where(e => e.Marked).ToList();
        if (marked.Count == 0)
        {
            return ImageBuildResult.None;
        }

        var warnings = new List<string>();
        var archives = new List<RepackedArchive>();
        foreach (var group in marked.GroupBy(e => e.Archive, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = WriteArchive(project, new ArchiveFolders(gameDirectory, outputDirectory, originalOf), formats, group.Key, [.. group], warnings);
            if (count > 0)
            {
                archives.Add(new RepackedArchive(group.Key, count));
            }
        }

        return new ImageBuildResult(archives, warnings);
    }

    private sealed record ArchiveFolders(string GameDirectory, string OutputDirectory, Func<string, string>? OriginalOf);

    private static int WriteArchive(
        ProjectFolder project, ArchiveFolders folders, IReadOnlyList<IArchiveFormat> formats, string archiveName, List<ImageEdit> edits, List<string> warnings)
    {
        var (gameDirectory, outputDirectory, originalOf) = folders;
        string gameFile;
        try
        {
            gameFile = GamePaths.Resolve(gameDirectory, archiveName);
        }
        catch (ArgumentException ex)
        {
            warnings.Add($"{archiveName}: {ex.Message}");
            return 0;
        }

        var target = Path.Combine(Path.GetFullPath(outputDirectory), archiveName.Replace('/', Path.DirectorySeparatorChar));
        var source = File.Exists(target) ? target : originalOf?.Invoke(archiveName) ?? gameFile;
        if (!File.Exists(source))
        {
            warnings.Add($"{archiveName}: o pacote não existe na pasta do jogo; as {edits.Count} edição(ões) de imagem ficaram de fora.");
            return 0;
        }

        var temp = target + ".imagens.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            int replaced;
            using (var archive = ArchiveOpener.Open(source, formats))
            {
                var replacements = Collect(project, archive, archiveName, edits, warnings);
                replaced = replacements.Count;
                if (replaced == 0)
                {
                    return 0;
                }

                using var output = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                archive.WriteTo(output, replacements);
            }

            File.Move(temp, target, overwrite: true);
            return replaced;
        }
        catch (Exception ex) when (ArchiveOpener.IsReadFailure(ex) || ex is NotSupportedException or ArgumentException)
        {
            throw new VNForgeException($"Não foi possível gravar as imagens editadas em {archiveName}: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static Dictionary<string, byte[]> Collect(ProjectFolder project, IArchive archive, string archiveName, List<ImageEdit> edits, List<string> warnings)
    {
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Path, edit.Path, StringComparison.Ordinal));
            if (entry is null)
            {
                warnings.Add($"{archiveName}/{edit.Path}: a imagem não existe mais no pacote; a edição ficou de fora.");
                continue;
            }

            var edited = project.EditedImagePath(archiveName, edit.Path);
            if (!File.Exists(edited))
            {
                warnings.Add($"{archiveName}/{edit.Path}: a edição está marcada, mas a imagem pronta não está em images/; abra a imagem no editor e marque de novo.");
                continue;
            }

            var current = Convert.ToHexStringLower(SHA256.HashData(ImageBytes.Read(archive, entry)));
            if (!string.Equals(current, edit.Hash, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{archiveName}/{edit.Path}: a imagem do jogo mudou depois da edição; a edição ficou de fora. Abra a imagem no editor e refaça.");
                continue;
            }

            replacements[entry.Path] = File.ReadAllBytes(edited);
        }

        return replacements;
    }
}
