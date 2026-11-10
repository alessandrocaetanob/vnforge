using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Services;

/// <summary>
/// A lista do VNDB no app: o token no cofre do Windows, a leitura e a gravação do estado da lista de cada jogo no <c>library.json</c> e a sincronização com o VNDB.
/// Faz o mesmo que <c>vnf library sync</c> e <c>vnf library status</c> (a regra de conflito é a do <see cref="LibraryListSynchronizer"/>); o token só passa pela memória.
/// </summary>
public sealed class VndbListService(IVndbTokenStore store, IVndbUserList vndb, LibraryFile file, TimeProvider? time = null, Func<string, string?>? readEnvironment = null, VndbSyncSettingsFile? settings = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly LibraryListSynchronizer synchronizer = new(vndb, time);
    private VndbSyncChoices choices = settings?.Load() ?? new VndbSyncChoices();

    /// <summary>O que a sincronização leva e se terminar um jogo aqui o marca no VNDB.</summary>
    public VndbSyncChoices Choices
    {
        get => choices;
        set
        {
            choices = value;
            settings?.Save(value);
        }
    }

    /// <summary>O token da variável de ambiente ou do cofre. Um cofre que não abre (outro usuário do Windows, outra máquina) vira "sem token" com o aviso, não uma exceção.</summary>
    public ResolvedVndbToken Token()
    {
        try
        {
            return VndbTokenResolver.Resolve(store, readEnvironment);
        }
        catch (VndbException e)
        {
            return new ResolvedVndbToken(null, VndbTokenSource.None) { Notice = e.Message };
        }
    }

    /// <summary>Confere o token no VNDB e, se ele vale, guarda no cofre.</summary>
    public async Task<VndbAuthInfo> SaveTokenAsync(string token, CancellationToken ct = default)
    {
        token = token.Trim();
        var account = await vndb.GetAuthInfoAsync(token, ct);
        store.Save(token);
        return account;
    }

    public bool RemoveToken() => store.Delete();

    public Task<VndbAuthInfo> CheckAsync(string token, CancellationToken ct = default) => vndb.GetAuthInfoAsync(token, ct);

    /// <param name="resolutions">Quem vale em cada conflito que a pessoa já decidiu; sem a chave vale o lado mais recente.</param>
    public Task<LibrarySyncPlan> PlanAsync(string token, LibrarySyncDirection direction, string? gameId = null, IReadOnlyDictionary<(string GameId, string Field), LibrarySyncWinner>? resolutions = null, CancellationToken ct = default) =>
        synchronizer.PlanAsync(
            file.Load(),
            token,
            new LibrarySyncOptions
            {
                Direction = direction,
                GameId = gameId,
                Fields = choices.Fields,
                Resolutions = resolutions ?? new Dictionary<(string GameId, string Field), LibrarySyncWinner>(),
            },
            ct);

    public Task<LibrarySyncReport> ApplyAsync(LibrarySyncPlan plan, string token, CancellationToken ct = default) =>
        synchronizer.ApplyAsync(plan, token, (game, state) => file.Update(current => LibraryListSynchronizer.Keep(current, game, state)), null, ct);

    /// <summary>Grava a mudança só aqui (como <c>vnf library status</c>): o VNDB só a recebe na próxima sincronização.</summary>
    /// <exception cref="LibraryException">O jogo não está confirmado no VNDB.</exception>
    public LibraryGame Edit(string gameId, VndbListUpdate update) =>
        file.Update(current =>
        {
            var game = current.Find(gameId);
            if (!game.IsConfirmed)
            {
                throw new LibraryException($"'{game.Title}' não tem jogo do VNDB confirmado; confirme o jogo antes de mexer na lista.");
            }

            var changed = game with { List = LibraryListState.Edit(game.CurrentList, game.VndbId!, update, clock.GetUtcNow()) };
            current.Replace(changed);
            return changed;
        });

    public LibraryGame Game(string gameId) => file.Load().Find(gameId);

    /// <summary>
    /// Grava a mudança só aqui e, se ela põe o rótulo Terminado e a pessoa ligou "marcar como terminado no VNDB", envia só esse rótulo ao VNDB na hora. Sem token, sem a permissão
    /// de gravar ou sem rede, a marca fica só aqui. Devolve o que dizer à pessoa quando o envio não foi feito, ou nulo.
    /// </summary>
    public async Task<string?> EditAsync(string gameId, VndbListUpdate update, CancellationToken ct = default)
    {
        Edit(gameId, update);
        if (!choices.MarkFinishedOnVndb || !update.LabelsSet.Contains(LibraryListLabels.Finished))
        {
            return null;
        }

        var token = Token().Token;
        if (token is null)
        {
            return "Gravado só aqui: sem o token do VNDB em Ajustes, nada foi enviado.";
        }

        try
        {
            // Só o rótulo: voto, datas e notas mudados no site não são sobrescritos, e o resto segue para a próxima sincronização.
            await vndb.UpdateEntryAsync(token, Game(gameId).VndbId!, new VndbListUpdate { LabelsSet = [LibraryListLabels.Finished] }, ct);
            return null;
        }
        catch (Exception e) when (e is VndbException or LibraryException or HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return "Gravado só aqui. " + e.Message;
        }
    }

    /// <summary>Tira o Jogando, põe o Terminado e a data de fim de hoje (se ainda não havia), como <see cref="EditAsync"/>.</summary>
    public Task<string?> MarkFinishedAsync(string gameId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return EditAsync(
            gameId,
            new VndbListUpdate
            {
                LabelsSet = [LibraryListLabels.Finished],
                LabelsUnset = [LibraryListLabels.Playing],
                Finished = Game(gameId).CurrentList?.Fields.Finished is null ? today : default,
            },
            ct);
    }

    /// <summary>Quantos jogos têm lista guardada e quando foi a última sincronização de qualquer um deles.</summary>
    public (int Games, DateTimeOffset? LastSync) Summary()
    {
        var states = file.Load().Games.Select(g => g.CurrentList).OfType<LibraryListState>().ToList();
        return (states.Count, states.Max(s => s.SyncedAt));
    }
}
