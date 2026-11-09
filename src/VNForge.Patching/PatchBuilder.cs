namespace VNForge.Patching;

/// <summary>Gera o <c>.vnfpatch</c>: um delta por arquivo que muda, nunca o arquivo inteiro do jogo.</summary>
public static class PatchBuilder
{
    /// <summary>Calcula os deltas e escreve o pacote em <paramref name="package"/>. A saída é a mesma para as mesmas entradas.</summary>
    /// <exception cref="PatchException">Dois arquivos com o mesmo caminho, ou uma falha ao calcular a diferença.</exception>
    public static PatchManifest Build(
        PatchInfo info,
        IReadOnlyList<PatchSource> sources,
        Stream package,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(package);

        var ordered = sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0)
        {
            throw new PatchException("Não há arquivo alterado: o build é igual ao jogo, e não há o que pôr no patch.");
        }

        if (ordered.Select(s => s.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Count)
        {
            throw new PatchException("Dois arquivos do patch têm o mesmo caminho.");
        }

        using var payload = new MemoryStream();
        var entries = new List<PatchFileEntry>();
        for (var i = 0; i < ordered.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ordered[i];
            progress?.Report(new PatchProgress(PatchPhase.Diff, source.RelativePath, i, ordered.Count));
            GamePaths.Resolve(Path.GetTempPath(), source.RelativePath);

            var offset = payload.Position;
            using var result = new FileStream(source.ResultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.OriginalPath is null)
            {
                DeltaCodec.EncodeNew(result, payload);
            }
            else
            {
                using var original = new FileStream(source.OriginalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                DeltaCodec.Encode(original, result, payload);
            }

            entries.Add(new PatchFileEntry(
                source.RelativePath,
                source.OriginalPath is null ? null : GamePaths.Sha256(source.OriginalPath),
                source.OriginalPath is null ? 0 : new FileInfo(source.OriginalPath).Length,
                GamePaths.Sha256(source.ResultPath),
                new FileInfo(source.ResultPath).Length,
                offset,
                payload.Position - offset));
        }

        progress?.Report(new PatchProgress(PatchPhase.Diff, null, ordered.Count, ordered.Count));
        var manifest = new PatchManifest(info.GameName, info.Version, info.PreviousVersion, info.Credits, info.ReadMe, entries);
        PatchPackage.Write(package, manifest, payload);
        return manifest;
    }
}
