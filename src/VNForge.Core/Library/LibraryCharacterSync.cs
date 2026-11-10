using VNForge.Core.Factsheet;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>Um jogo do VNDB que a sincronização não conseguiu trazer; os outros seguem.</summary>
public sealed record LibraryCharacterSyncFailure(string VisualNovelId, string Message);

/// <summary>Andamento da sincronização: <paramref name="Done"/> de <paramref name="Total"/> jogos do VNDB.</summary>
public sealed record LibraryCharacterSyncProgress(int Done, int Total, string VisualNovelId);

/// <summary>
/// O que a sincronização fez. <paramref name="Games"/> são os jogos do VNDB distintos da biblioteca; <paramref name="Fetched"/> os que foram ao VNDB e <paramref name="Cached"/> os
/// que já tinham ficha guardada. <paramref name="Characters"/> conta os personagens trazidos e <paramref name="EditionsMatched"/> os itens da biblioteca cuja edição foi casada agora. <paramref name="EditionsIncomplete"/> conta os itens cujo casamento foi feito com a lista de edições cortada pelo limite do cliente (vale, mas <c>again</c> o refaz).
/// </summary>
public sealed record LibraryCharacterSyncResult(int Games, int Fetched, int Cached, int Characters, int EditionsMatched, IReadOnlyList<LibraryCharacterSyncFailure> Failures, int EditionsIncomplete = 0);

/// <summary>
/// Enche o registro de personagens da Biblioteca. Para cada jogo do VNDB que a pessoa tem confirmado, traz a ficha (<see cref="SheetFetcher"/>: jogo, personagens com o campo
/// <c>vns</c> e traços, sem perder tradução) e casa a edição de cada pasta com as edições do VNDB (<see cref="LibraryEditionMatcher"/>). O que já está guardado não volta ao VNDB
/// a não ser com <c>again</c>: o cliente do VNDB já limita os pedidos pela fila (200 a cada 5 minutos), e guardar a ficha e as edições poupa a fila numa segunda rodada.
/// </summary>
public static class LibraryCharacterSync
{
    /// <exception cref="LibraryException">Um arquivo de ficha ou de edições está ilegível ou não pôde ser gravado.</exception>
    public static async Task<LibraryCharacterSyncResult> SyncAsync(
        IVndbCatalog catalog,
        GameLibrary library,
        SheetStore store,
        GameEditionsFile editionsFile,
        bool again = false,
        TimeProvider? time = null,
        IProgress<LibraryCharacterSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(editionsFile);
        var clock = time ?? TimeProvider.System;
        var groups = library.Games.Where(g => g.VndbId is not null).GroupBy(g => g.VndbId!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        int fetched = 0, cached = 0, characters = 0, matched = 0, incomplete = 0, done = 0;
        var failures = new List<LibraryCharacterSyncFailure>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!again && HasEditions(store, group.Key))
                {
                    cached++;
                }
                else if (await catalog.GetVisualNovelAsync(group.Key, cancellationToken).ConfigureAwait(false) is { } vn)
                {
                    var found = await catalog.GetCharactersAsync(group.Key, cancellationToken).ConfigureAwait(false);
                    var now = clock.GetUtcNow();
                    characters += store.Update(s => SheetFetcher.Merge(s, vn, found, now)).Characters;
                    fetched++;
                }
                else
                {
                    failures.Add(new LibraryCharacterSyncFailure(group.Key, $"O VNDB não tem o jogo {group.Key}."));
                    progress?.Report(new LibraryCharacterSyncProgress(++done, groups.Count, group.Key));
                    continue;
                }

                var pending = group.Where(g => again || !editionsFile.Load().IsCurrent(g)).ToList();
                if (pending.Count > 0)
                {
                    var releases = await catalog.GetReleasesAsync(group.Key, cancellationToken).ConfigureAwait(false);
                    var partial = releases.Truncated;
                    var stamp = clock.GetUtcNow();
                    editionsFile.Update(editions =>
                    {
                        foreach (var game in pending)
                        {
                            var found = LibraryEditionMatcher.Match(game, releases.Items);
                            // Com a lista cortada a edição certa pode ter ficado de fora; o casamento só decide o papel, então vale, marcado como incompleto.
                            editions.Set(game.Id, group.Key, found, stamp, partial);
                            matched += found.Count > 0 ? 1 : 0;
                            incomplete += partial ? 1 : 0;
                        }
                    });
                }
            }
            catch (VndbException e)
            {
                failures.Add(new LibraryCharacterSyncFailure(group.Key, e.Message));
            }

            progress?.Report(new LibraryCharacterSyncProgress(++done, groups.Count, group.Key));
        }

        return new LibraryCharacterSyncResult(groups.Count, fetched, cached, characters, matched, failures, incomplete);
    }

    /// <summary>A ficha está guardada e todas as aparições dos personagens nela já trazem as edições (as gravadas antes disso só têm o resumo, com o spoiler mais baixo, e precisam ser buscadas de novo).</summary>
    private static bool HasEditions(SheetStore store, string visualNovelId) =>
        store.LoadGame(visualNovelId) is { } sheet && store.LoadCharacters(sheet.CharacterIds).All(c => c.In(visualNovelId)?.Editions is { Count: > 0 });
}
