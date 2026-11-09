namespace VNForge.Patching;

/// <param name="Identical">Arquivos que, com o patch aplicado, ficaram idênticos ao build.</param>
/// <param name="RestoredToOriginal">Arquivos do jogo que a desinstalação devolveu idênticos ao original.</param>
/// <param name="RemovedCreated">Arquivos criados pelo patch que a desinstalação tirou.</param>
public sealed record PatchVerification(int Identical, int RestoredToOriginal, int RemovedCreated);

/// <summary>
/// A conferência do lançamento: aplica o patch numa cópia limpa dos originais, compara cada arquivo com o do build, desinstala e compara com o original.
/// Roda o mesmo <see cref="PatchInstaller"/> que o jogador vai rodar.
/// </summary>
public static class PatchVerifier
{
    /// <param name="workDirectory">Pasta de trabalho, que não pode existir ainda; é apagada ao fim.</param>
    /// <exception cref="PatchException">Algum arquivo não ficou idêntico ao build, ou a desinstalação não devolveu o original.</exception>
    public static PatchVerification Verify(PatchPackage package, IReadOnlyList<PatchSource> sources, string workDirectory, IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        if (Directory.Exists(workDirectory))
        {
            throw new PatchException($"A pasta de verificação '{workDirectory}' já existe.");
        }

        var game = Path.Combine(workDirectory, "jogo");
        try
        {
            foreach (var source in sources.Where(s => s.OriginalPath is not null))
            {
                var copy = GamePaths.Resolve(game, source.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(source.OriginalPath!, copy);
            }

            Directory.CreateDirectory(game);
            PatchInstaller.Install(game, package, progress, cancellationToken);
            foreach (var source in sources)
            {
                if (GamePaths.ShaOrNull(GamePaths.Resolve(game, source.RelativePath)) != GamePaths.Sha256(source.ResultPath))
                {
                    throw new PatchException($"Verificação falhou: {source.RelativePath} aplicado não é idêntico ao do build.");
                }
            }

            var result = PatchInstaller.Uninstall(game, progress, cancellationToken);
            if (result.Kept.Count > 0)
            {
                throw new PatchException($"Verificação falhou: a desinstalação deixou {string.Join(", ", result.Kept)} sem devolver.");
            }

            var restored = 0;
            var removed = 0;
            foreach (var source in sources)
            {
                var target = GamePaths.Resolve(game, source.RelativePath);
                if (source.OriginalPath is null)
                {
                    removed += File.Exists(target) ? throw new PatchException($"Verificação falhou: a desinstalação não tirou {source.RelativePath}.") : 1;
                    continue;
                }

                if (GamePaths.ShaOrNull(target) != GamePaths.Sha256(source.OriginalPath))
                {
                    throw new PatchException($"Verificação falhou: a desinstalação não devolveu o original de {source.RelativePath}.");
                }

                restored++;
            }

            return new PatchVerification(sources.Count, restored, removed);
        }
        finally
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }
}
