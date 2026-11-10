namespace VNForge.Core.Library;

/// <summary>O que uma varredura da Biblioteca fez: o que achou, o que mudou na biblioteca, os projetos ligados e, se a pessoa pediu, o casamento com o VNDB.</summary>
public sealed record LibraryScanOutcome(LibraryScanResult Scan, LibraryMergeResult Merge, IReadOnlyList<LibraryProjectLink> Linked, LibraryMatchRun? Match);

/// <summary>
/// A varredura da tela e a da CLI (<c>vnf library scan</c> e <c>vnf library match</c>) pelo mesmo caminho: varre a pasta, junta ao <c>library.json</c> sob a trava do arquivo, liga os
/// projetos de tradução que já existem e, com <see cref="LibraryRoot.MatchVndb"/>, casa os jogos novos com o VNDB, gravando jogo a jogo para que uma pausa não perca o que já foi feito.
/// Só lê nomes de arquivo da pasta dos jogos; não escreve nela.
/// </summary>
/// <param name="matcherFactory">Cria o casamento com o VNDB quando a varredura vai casar; nulo desliga o casamento.</param>
/// <param name="recentProjects">As pastas dos projetos de tradução conhecidos, para ligar a cada jogo.</param>
public sealed class LibraryScanService(LibraryFile file, LibraryScanner scanner, Func<LibraryMatcher>? matcherFactory, Func<IReadOnlyList<string>> recentProjects, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Varre <paramref name="root"/>. Parar pelo <paramref name="cancellationToken"/> não lança: o que já foi achado entra na biblioteca (<see cref="LibraryScanResult.Cancelled"/>) e o casamento
    /// não começa. O casamento parado no meio também guarda o que casou. <paramref name="onScanned"/> avisa que a leitura das pastas terminou e o casamento (se houver) vai começar.
    /// </summary>
    /// <exception cref="LibraryException">A pasta não existe ou o <c>library.json</c> está ilegível.</exception>
    public async Task<LibraryScanOutcome> RunAsync(
        LibraryRoot root,
        IProgress<LibraryScanProgress>? scanProgress = null,
        IProgress<LibraryMatchProgress>? matchProgress = null,
        Action<LibraryScanResult>? onScanned = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var options = new LibraryScanOptions(root.IncludeSubfolders ? new LibraryScanOptions().MaxDepth : 0, IncludeUnknown: true, EstimateFolders: true);
        var scan = await Task.Run(() => scanner.Scan(root.Path, options, scanProgress, cancellationToken), CancellationToken.None).ConfigureAwait(false);

        onScanned?.Invoke(scan);
        var recent = recentProjects();
        var (merge, linked) = file.Update(library =>
        {
            library.SetRoot(root with { ScannedAt = _time.GetUtcNow() });
            var merged = library.Merge(scan.Games, root.Path);
            return (merged, LibraryProjectLinker.LinkAll(library, recent));
        });

        LibraryMatchRun? match = null;
        if (root.MatchVndb && matcherFactory is not null && !scan.Cancelled)
        {
            var library = file.Load();
            match = await matcherFactory()
                .MatchAllAsync(library, new LibraryMatchOptions { AutoConfirm = true }, progress: matchProgress, onMatched: game => file.Update(current => current.Replace(game)), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return new LibraryScanOutcome(scan, merge, linked, match);
    }
}
