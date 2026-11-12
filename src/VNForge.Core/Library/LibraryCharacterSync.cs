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

/// <summary>Como a sincronização roda.</summary>
/// <param name="Again">Traz de novo o que já está guardado, em vez de pular.</param>
/// <param name="Time">O relógio que carimba o que é guardado; nulo usa o do sistema.</param>
/// <param name="Progress">Recebe um relato a cada jogo do VNDB concluído.</param>
public sealed record LibraryCharacterSyncOptions(bool Again = false, TimeProvider? Time = null, IProgress<LibraryCharacterSyncProgress>? Progress = null);

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
        LibraryCharacterSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(editionsFile);
        var groups = library.Games.Where(g => g.VndbId is not null).GroupBy(g => g.VndbId!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var run = new Run(catalog, store, editionsFile, options?.Again ?? false, options?.Time ?? TimeProvider.System, cancellationToken);
        var done = 0;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await run.SyncGroupAsync(group).ConfigureAwait(false);
            options?.Progress?.Report(new LibraryCharacterSyncProgress(++done, groups.Count, group.Key));
        }

        return run.ResultFor(groups.Count);
    }

    /// <summary>O estado de uma rodada: o que ela usa em cada jogo do VNDB e as contagens que o resultado devolve.</summary>
    private sealed class Run(IVndbCatalog catalog, SheetStore store, GameEditionsFile editionsFile, bool again, TimeProvider clock, CancellationToken cancellationToken)
    {
        private readonly List<LibraryCharacterSyncFailure> _failures = [];
        private int _fetched;
        private int _cached;
        private int _characters;
        private int _matched;
        private int _incomplete;

        public LibraryCharacterSyncResult ResultFor(int games) => new(games, _fetched, _cached, _characters, _matched, _failures, _incomplete);

        public async Task SyncGroupAsync(IGrouping<string, LibraryGame> group)
        {
            try
            {
                if (await FetchSheetAsync(group.Key).ConfigureAwait(false))
                {
                    await MatchEditionsAsync(group).ConfigureAwait(false);
                }
            }
            catch (VndbException e)
            {
                _failures.Add(new LibraryCharacterSyncFailure(group.Key, e.Message));
            }
        }

        /// <summary>Falso quando o VNDB não tem o jogo: a falha fica registrada e as edições não são casadas.</summary>
        private async Task<bool> FetchSheetAsync(string visualNovelId)
        {
            if (!again && HasEditions(visualNovelId))
            {
                _cached++;
                return true;
            }

            if (await catalog.GetVisualNovelAsync(visualNovelId, cancellationToken).ConfigureAwait(false) is not { } vn)
            {
                _failures.Add(new LibraryCharacterSyncFailure(visualNovelId, $"O VNDB não tem o jogo {visualNovelId}."));
                return false;
            }

            var found = await catalog.GetCharactersAsync(visualNovelId, cancellationToken).ConfigureAwait(false);
            var now = clock.GetUtcNow();
            _characters += store.Update(s => SheetFetcher.Merge(s, vn, found, now)).Characters;
            _fetched++;
            return true;
        }

        private async Task MatchEditionsAsync(IGrouping<string, LibraryGame> group)
        {
            var pending = group.Where(g => again || !editionsFile.Load().IsCurrent(g)).ToList();
            if (pending.Count == 0)
            {
                return;
            }

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
                    _matched += found.Count > 0 ? 1 : 0;
                    _incomplete += partial ? 1 : 0;
                }
            });
        }

        /// <summary>A ficha está guardada e todas as aparições dos personagens nela já trazem as edições (as gravadas antes disso só têm o resumo, com o spoiler mais baixo, e precisam ser buscadas de novo).</summary>
        private bool HasEditions(string visualNovelId) =>
            store.LoadGame(visualNovelId) is { } sheet && store.LoadCharacters(sheet.CharacterIds).All(c => c.In(visualNovelId)?.Editions is { Count: > 0 } && c.AgeFetched);
    }
}
