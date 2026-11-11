using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>Para onde a sincronização anda: nos dois sentidos, só trazendo do VNDB ou só enviando o que mudou aqui.</summary>
public enum LibrarySyncDirection
{
    Both,
    Pull,
    Push,
}

/// <summary>Os campos da lista que a sincronização leva de um lado ao outro; o que fica de fora segue como está nos dois lados.</summary>
[Flags]
public enum LibrarySyncFields
{
    None = 0,
    Labels = 1,
    Vote = 2,
    Dates = 4,
    Notes = 8,
    All = Labels | Vote | Dates | Notes,
}

public enum LibrarySyncAction
{
    Unchanged,
    Pulled,
    Pushed,

    /// <summary>Trouxe campos do VNDB e enviou outros: cada lado tinha mudado campos diferentes.</summary>
    Merged,

    Failed,
}

public enum LibrarySyncWinner
{
    Local,
    Remote,
}

/// <summary>Um campo mudado dos dois lados desde a última sincronização, com valores diferentes, e quem ficou com ele.</summary>
public sealed record LibrarySyncConflict(string Field, string Local, string Remote, LibrarySyncWinner Winner);

/// <param name="Game">O jogo como estava antes da sincronização.</param>
/// <param name="Pulled">Os campos que o app recebe do VNDB.</param>
/// <param name="Pushed">Os campos que o VNDB recebe do app.</param>
public sealed record LibrarySyncItem(
    LibraryGame Game,
    LibrarySyncAction Action,
    IReadOnlyList<string> Pulled,
    IReadOnlyList<string> Pushed,
    IReadOnlyList<LibrarySyncConflict> Conflicts,
    string? Error = null)
{
    /// <summary>A entrada existia no VNDB na última sincronização e sumiu (apagada no site).</summary>
    public bool RemovedOnSite { get; init; }

    /// <summary>O jogo tem mudança daqui que o VNDB ainda não recebeu (antes desta sincronização).</summary>
    public bool HasLocalEdits { get; init; }
}

public sealed record LibrarySyncOptions
{
    public LibrarySyncDirection Direction { get; init; }

    /// <summary>Só este jogo da biblioteca (pelo id).</summary>
    public string? GameId { get; init; }

    /// <summary>
    /// Recria no VNDB a entrada que foi apagada no site e tem mudança aqui, mandando tudo o que o app guarda dela. Sem isto a entrada apagada nunca é recriada: o app não
    /// desfaz uma exclusão feita no site sem a pessoa pedir. Só vale para quem tem edição pendente; sem edição a exclusão segue a regra normal (o sentido que traz a consome,
    /// o que só envia a deixa como está). Não vale com <see cref="LibrarySyncDirection.Pull"/>.
    /// </summary>
    public bool RecreateRemoved { get; init; }

    /// <summary>Os campos que a sincronização leva; o padrão é todos. Um campo de fora não é lido nem enviado e não entra em conflito.</summary>
    public LibrarySyncFields Fields { get; init; } = LibrarySyncFields.All;

    /// <summary>
    /// Quem vale em cada conflito que a pessoa já decidiu, por jogo (id da biblioteca) e campo (o nome em <see cref="LibrarySyncConflict.Field"/>). Sem a chave, vale o lado mais recente,
    /// como sempre.
    /// </summary>
    public IReadOnlyDictionary<(string GameId, string Field), LibrarySyncWinner> Resolutions { get; init; } = new Dictionary<(string GameId, string Field), LibrarySyncWinner>();
}

public sealed record LibrarySyncProgress(int Done, int Total, LibraryGame Game);

/// <summary>
/// O que a sincronização vai fazer, calculado sem escrever nada, nem no VNDB nem na biblioteca. <see cref="Account"/> é a conta do token; <see cref="RemoteOnly"/> conta
/// as entradas da lista do VNDB cujo jogo não está confirmado na biblioteca (a sincronização não as traz).
/// </summary>
public sealed class LibrarySyncPlan
{
    internal LibrarySyncPlan(VndbAuthInfo account, LibrarySyncDirection direction, IReadOnlyList<LibrarySyncStep> steps, int remoteOnly)
    {
        Account = account;
        Direction = direction;
        Steps = steps;
        RemoteOnly = remoteOnly;
    }

    public VndbAuthInfo Account { get; }

    public LibrarySyncDirection Direction { get; }

    public int RemoteOnly { get; }

    public IReadOnlyList<LibrarySyncItem> Items => [.. Steps.Select(s => s.Item)];

    /// <summary>Quantos jogos recebem uma gravação no VNDB.</summary>
    public int PushCount => Steps.Count(s => s.Update is not null);

    internal IReadOnlyList<LibrarySyncStep> Steps { get; }
}

internal sealed record LibrarySyncStep(LibrarySyncItem Item, LibraryListState? NewState, bool StateChanged, VndbListUpdate? Update);

/// <param name="Items">O que foi feito, jogo a jogo. Se a sincronização parou no meio, só os jogos até onde ela chegou.</param>
public sealed record LibrarySyncReport(VndbAuthInfo Account, IReadOnlyList<LibrarySyncItem> Items, int RemoteOnly, bool Cancelled, string? AbortReason);

/// <summary>
/// Sincroniza a lista do VNDB (rótulos, voto, datas, notas) com o estado de cada jogo confirmado da biblioteca. Só mexe nos jogos que a biblioteca já tem confirmados:
/// nunca acrescenta um jogo à lista do VNDB que a pessoa não marcou aqui, e só grava no VNDB quando quem chama pede um sentido que envia
/// (<see cref="LibrarySyncDirection.Both"/> ou <see cref="LibrarySyncDirection.Push"/>), de preferência depois de mostrar o <see cref="LibrarySyncPlan"/>.
/// <para>
/// Regra de conflito. Cada campo é comparado com o acordo da última sincronização (<see cref="LibraryListState.Base"/>): se só um lado mudou, a mudança passa para o
/// outro. Se os dois mudaram o mesmo campo para valores diferentes, é conflito: nos dois sentidos vence o lado mais recente (a hora em que a pessoa editou aqui contra a
/// última modificação da entrada no VNDB; empate fica com o VNDB) e o conflito sai no relatório como aviso. Com <see cref="LibrarySyncDirection.Pull"/> o VNDB vence os
/// conflitos e com <see cref="LibrarySyncDirection.Push"/> vence o app: quem escolhe o sentido já disse de que lado está. Rótulos nunca entram em conflito, porque cada
/// um é sim ou não: mudar um rótulo de um lado só passa para o outro, e na primeira sincronização (sem acordo anterior) os rótulos dos dois lados se somam. Na primeira
/// sincronização, um campo vazio de um lado recebe o do outro e só dois valores diferentes viram conflito.
/// </para>
/// <para>
/// O que um sentido não aplica fica como está e continua pendente para a próxima sincronização.
/// </para>
/// </summary>
public sealed class LibraryListSynchronizer(IVndbUserList vndb, TimeProvider? time = null)
{
    private static readonly Scalar[] Scalars =
    [
        new("voto", f => f.Vote, (f, v) => f with { Vote = (int?)v }, v => v is int vote ? LibraryListRules.FormatVote(vote) : "nenhum", LibrarySyncFields.Vote),
        new("início", f => f.Started, (f, v) => f with { Started = (DateOnly?)v }, ShowDate, LibrarySyncFields.Dates),
        new("fim", f => f.Finished, (f, v) => f with { Finished = (DateOnly?)v }, ShowDate, LibrarySyncFields.Dates),
        new("notas", f => f.Notes, (f, v) => f with { Notes = (string?)v }, ShowNotes, LibrarySyncFields.Notes),
    ];

    private readonly IVndbUserList _vndb = vndb ?? throw new ArgumentNullException(nameof(vndb));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private enum Move
    {
        Keep,
        Pull,
        Push,
    }

    /// <summary>Lê a lista do VNDB e calcula o que mudaria, sem gravar nada.</summary>
    /// <exception cref="LibraryException">O token não tem as permissões, a lista passou do limite da leitura ou o jogo pedido não está confirmado.</exception>
    /// <exception cref="VndbException">O VNDB recusou o token ou não respondeu.</exception>
    public async Task<LibrarySyncPlan> PlanAsync(GameLibrary library, string token, LibrarySyncOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(options);

        var games = Selected(library, options);
        var account = await _vndb.GetAuthInfoAsync(token, ct).ConfigureAwait(false);
        if (!account.Permissions.Contains("listread"))
        {
            throw new LibraryException("O token não tem a permissão listread. Gere outro em vndb.org/u/tokens com a leitura da lista e rode vnf vndb token set.");
        }

        var list = await _vndb.GetListAsync(account.UserId, token, ct).ConfigureAwait(false);
        if (list.Truncated)
        {
            throw new LibraryException("A lista do VNDB passou do limite de leitura (20 mil entradas) e a sincronização não consegue vê-la inteira.");
        }

        var remote = list.Items.ToDictionary(e => e.VisualNovelId, StringComparer.Ordinal);
        var now = _time.GetUtcNow();
        var steps = games.Select(g => Plan(g, remote.GetValueOrDefault(g.VndbId!), options, now)).ToList();
        if (steps.Any(s => s.Update is not null) && !account.Permissions.Contains("listwrite"))
        {
            throw new LibraryException("O token não tem a permissão listwrite, e há mudanças para enviar. Gere outro em vndb.org/u/tokens com a escrita da lista, ou use --pull.");
        }

        var inLibrary = library.Games.Where(g => g.VndbId is not null).Select(g => g.VndbId!).ToHashSet(StringComparer.Ordinal);
        return new LibrarySyncPlan(account, options.Direction, steps, remote.Keys.Count(id => !inLibrary.Contains(id)));
    }

    /// <summary>
    /// Faz o que o <paramref name="plan"/> diz: envia as mudanças ao VNDB, jogo a jogo, e entrega cada estado novo a <paramref name="keep"/> assim que o jogo termina, para que
    /// quem grava o arquivo (dentro de <see cref="LibraryFile.Update{T}(Func{GameLibrary, T})"/>) não segure uma biblioteca velha durante a espera pela rede e não perca o que já foi feito.
    /// Um 401 ou três falhas seguidas param o resto; uma falha só deixa aquele jogo como estava. Veja <see cref="Keep"/> para uma <paramref name="keep"/> pronta.
    /// </summary>
    /// <param name="keep">Grava o estado novo do jogo e diz se gravou. Falso: o jogo mudou por outro lado enquanto a sincronização esperava, e o estado novo não vale.</param>
    public async Task<LibrarySyncReport> ApplyAsync(LibrarySyncPlan plan, string token, Func<LibraryGame, LibraryListState, bool> keep, IProgress<LibrarySyncProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var done = new List<LibrarySyncItem>();
        var pushes = plan.PushCount;
        var pushed = 0;
        var failures = 0;
        string? abort = null;
        var cancelled = false;
        foreach (var step in plan.Steps)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (step.Update is not null)
                {
                    progress?.Report(new LibrarySyncProgress(pushed, pushes, step.Item.Game));
                    ct.ThrowIfCancellationRequested();
                    await _vndb.UpdateEntryAsync(token, step.Item.Game.VndbId!, step.Update, ct).ConfigureAwait(false);
                    pushed++;
                    failures = 0;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (VndbException e)
            {
                done.Add(step.Item with { Action = LibrarySyncAction.Failed, Error = e.Message });
                failures++;
                if (e.StatusCode == 401)
                {
                    abort = "O VNDB não aceitou o token. Gere outro em vndb.org/u/tokens e rode vnf vndb token set.";
                    break;
                }

                if (failures >= 3)
                {
                    abort = "O VNDB falhou 3 vezes seguidas; a sincronização parou. Tente de novo mais tarde.";
                    break;
                }

                continue;
            }

            if (step.NewState is not null && step.StateChanged && !keep(step.Item.Game, step.NewState))
            {
                done.Add(step.Item with
                {
                    Action = LibrarySyncAction.Failed,
                    Error = "O jogo mudou aqui enquanto a sincronização esperava o VNDB; o estado não foi atualizado" + (step.Update is null ? "." : " (o envio foi feito). Rode de novo."),
                });
                continue;
            }

            done.Add(step.Item);
        }

        return new LibrarySyncReport(plan.Account, done, plan.RemoteOnly, cancelled, abort);
    }

    /// <summary>
    /// Troca o estado da lista do jogo na <paramref name="current"/> (a biblioteca recém-lida, dentro do <see cref="LibraryFile.Update{T}(Func{GameLibrary, T})"/>) mexendo só no <c>List</c>
    /// e deixando o resto do jogo como está agora. Se o jogo sumiu, teve o casamento trocado ou a lista editada desde o plano, não grava e devolve falso.
    /// </summary>
    public static bool Keep(GameLibrary current, LibraryGame planned, LibraryListState state)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(state);
        var now = current.Games.FirstOrDefault(g => g.Id == planned.Id);
        if (now is null || now.VndbId != planned.VndbId || !LibraryListState.SameAs(now.CurrentList, planned.CurrentList))
        {
            return false;
        }

        current.Replace(now with { List = state });
        return true;
    }

    private static List<LibraryGame> Selected(GameLibrary library, LibrarySyncOptions options)
    {
        if (options.GameId is null)
        {
            return [.. library.Games.Where(g => g.IsConfirmed)];
        }

        var game = library.Games.FirstOrDefault(g => g.Id == options.GameId)
            ?? throw new LibraryException($"O jogo {options.GameId} não está na biblioteca.");
        return game.IsConfirmed
            ? [game]
            : throw new LibraryException($"'{game.Title}' não tem jogo do VNDB confirmado; confirme com vnf library match --confirm antes de sincronizar a lista.");
    }

    private static string ShowDate(object? value) => value is DateOnly date ? date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "nenhuma";

    private static string ShowNotes(object? value)
    {
        if (value is not string { Length: > 0 } text)
        {
            return "nenhuma";
        }

        return text.Length <= 40 ? text : text[..40] + "...";
    }

    private static LibrarySyncStep Plan(LibraryGame game, VndbListEntry? entry, LibrarySyncOptions options, DateTimeOffset now)
    {
        var direction = options.Direction;
        var recreate = options.RecreateRemoved && direction != LibrarySyncDirection.Pull;
        var state = game.CurrentList;
        if (state is null && (entry is null || direction == LibrarySyncDirection.Push))
        {
            return new LibrarySyncStep(new LibrarySyncItem(game, LibrarySyncAction.Unchanged, [], [], []), null, false, null);
        }

        var removed = entry is null && state is { OnVndb: true };
        var recreating = removed && state!.Pending && recreate;
        if (removed && !recreating && (state!.Pending || direction == LibrarySyncDirection.Push))
        {
            // Apagada no site. Com edição aqui, recriar a entrada desfaria a exclusão e só acontece com pedido (RecreateRemoved, e só para quem tem edição).
            // Sem edição, quem traz a exclusão é o sentido que traz (Both ou Pull): o envio nunca a consome, e o estado fica como está para a próxima edição cair neste mesmo aviso.
            return new LibrarySyncStep(new LibrarySyncItem(game, LibrarySyncAction.Unchanged, [], [], []) { RemovedOnSite = true, HasLocalEdits = state.Pending }, null, false, null);
        }

        var local = state?.Fields ?? LibraryListFields.Empty;
        var remote = entry is null ? LibraryListFields.Empty : LibraryListFields.From(entry);
        var oldBase = recreating ? null : state?.Base;
        var hasBase = oldBase is not null;
        var baseFields = oldBase ?? LibraryListFields.Empty;
        var localNewer = (state?.ModifiedAt ?? DateTimeOffset.MinValue) > (entry?.LastModified ?? DateTimeOffset.MinValue);

        var pulled = new List<string>();
        var pushed = new List<string>();
        var conflicts = new List<LibrarySyncConflict>();
        var newLocal = local;
        var newRemote = remote;

        foreach (var scalar in Scalars.Where(s => options.Fields.HasFlag(s.Group)))
        {
            var l = scalar.Get(local);
            var r = scalar.Get(remote);
            LibrarySyncWinner? chosen = options.Resolutions.TryGetValue((game.Id, scalar.Name), out var decided) ? decided : null;
            switch (DecideScalar(scalar, new ScalarValues(l, r, scalar.Get(baseFields), hasBase), direction, localNewer, conflicts, chosen))
            {
                case Move.Pull:
                    newLocal = scalar.Set(newLocal, r);
                    pulled.Add(scalar.Name);
                    break;
                case Move.Push:
                    newRemote = scalar.Set(newRemote, l);
                    pushed.Add(scalar.Name);
                    break;
            }
        }

        var (labelsLocal, labelsRemote, labelsPulled, labelsPushed) = options.Fields.HasFlag(LibrarySyncFields.Labels)
            ? MergeLabels(local, remote, baseFields, hasBase, direction)
            : (local.Labels, remote.Labels, false, false);
        newLocal = newLocal with { Labels = labelsLocal };
        newRemote = newRemote with { Labels = labelsRemote };
        if (labelsPulled)
        {
            pulled.Insert(0, "rótulos");
        }

        if (labelsPushed)
        {
            pushed.Insert(0, "rótulos");
        }

        var update = BuildUpdate(remote, LibraryListFields.Normalize(newRemote));
        var newState = new LibraryListState(game.VndbId!, LibraryListFields.Normalize(newLocal), state?.ModifiedAt ?? entry?.LastModified ?? now)
        {
            Base = NewBase(newLocal, newRemote, baseFields),
            SyncedAt = now,
            OnVndb = entry is not null || update is not null,
        };
        var changed = state is null
            || oldBase is null
            || !state.Fields.SameAs(newState.Fields)
            || !state.Fields.Labels.SequenceEqual(newState.Fields.Labels)
            || !oldBase.SameAs(newState.Base)
            || state.OnVndb != newState.OnVndb;
        var action = (pulled.Count > 0, pushed.Count > 0) switch
        {
            (true, true) => LibrarySyncAction.Merged,
            (true, false) => LibrarySyncAction.Pulled,
            (false, true) => LibrarySyncAction.Pushed,
            _ => LibrarySyncAction.Unchanged,
        };
        var item = new LibrarySyncItem(game, action, pulled, pushed, conflicts) { RemovedOnSite = removed, HasLocalEdits = state?.Pending ?? false };
        return new LibrarySyncStep(item, newState, changed, update);
    }

    private sealed record ScalarValues(object? Local, object? Remote, object? Base, bool HasBase);

    private static Move DecideScalar(Scalar scalar, ScalarValues values, LibrarySyncDirection direction, bool localNewer, List<LibrarySyncConflict> conflicts, LibrarySyncWinner? chosen)
    {
        var (l, r, b, hasBase) = values;
        if (Equals(l, r))
        {
            return Move.Keep;
        }

        Move wanted;
        var conflict = false;
        if (hasBase)
        {
            var localChanged = !Equals(l, b);
            var remoteChanged = !Equals(r, b);
            wanted = (localChanged, remoteChanged) switch
            {
                (true, false) => Move.Push,
                (false, true) => Move.Pull,
                _ => Move.Keep,
            };
            conflict = localChanged && remoteChanged;
        }
        else if (r is null)
        {
            wanted = Move.Push;
        }
        else if (l is null)
        {
            wanted = Move.Pull;
        }
        else
        {
            wanted = Move.Keep;
            conflict = true;
        }

        if (conflict)
        {
            wanted = (chosen, direction) switch
            {
                (LibrarySyncWinner.Local, _) => Move.Push,
                (LibrarySyncWinner.Remote, _) => Move.Pull,
                (_, LibrarySyncDirection.Pull) => Move.Pull,
                (_, LibrarySyncDirection.Push) => Move.Push,
                _ => localNewer ? Move.Push : Move.Pull,
            };
            conflicts.Add(new LibrarySyncConflict(scalar.Name, scalar.Show(l), scalar.Show(r), wanted == Move.Push ? LibrarySyncWinner.Local : LibrarySyncWinner.Remote));
        }

        return (wanted, direction) switch
        {
            (Move.Push, LibrarySyncDirection.Pull) or (Move.Pull, LibrarySyncDirection.Push) => Move.Keep,
            _ => wanted,
        };
    }

    private static (IReadOnlyList<VndbListLabel> Local, IReadOnlyList<VndbListLabel> Remote, bool Pulled, bool Pushed) MergeLabels(
        LibraryListFields local,
        LibraryListFields remote,
        LibraryListFields baseFields,
        bool hasBase,
        LibrarySyncDirection direction)
    {
        var newLocal = local.Labels.ToDictionary(l => l.Id);
        var newRemote = remote.Labels.ToDictionary(l => l.Id);
        var pulled = false;
        var pushed = false;
        foreach (var id in local.Labels.Concat(remote.Labels).Select(l => l.Id).Distinct())
        {
            var inLocal = local.HasLabel(id);
            var inRemote = remote.HasLabel(id);
            if (inLocal == inRemote)
            {
                continue;
            }

            var push = hasBase ? inLocal != baseFields.HasLabel(id) : inLocal;
            if (push && direction != LibrarySyncDirection.Pull)
            {
                pushed = true;
                if (inLocal)
                {
                    newRemote[id] = local.Labels.First(l => l.Id == id);
                }
                else
                {
                    newRemote.Remove(id);
                }
            }
            else if (!push && direction != LibrarySyncDirection.Push)
            {
                pulled = true;
                if (inRemote)
                {
                    newLocal[id] = remote.Labels.First(l => l.Id == id);
                }
                else
                {
                    newLocal.Remove(id);
                }
            }
        }

        // Um rótulo que os dois têm fica com o nome que o VNDB dá a ele.
        foreach (var label in remote.Labels.Where(l => newLocal.ContainsKey(l.Id)))
        {
            newLocal[label.Id] = label;
        }

        return ([.. newLocal.Values.OrderBy(l => l.Id)], [.. newRemote.Values.OrderBy(l => l.Id)], pulled, pushed);
    }

    private static LibraryListFields NewBase(LibraryListFields newLocal, LibraryListFields newRemote, LibraryListFields oldBase)
    {
        var settled = oldBase;
        foreach (var scalar in Scalars)
        {
            var l = scalar.Get(newLocal);
            if (Equals(l, scalar.Get(newRemote)))
            {
                settled = scalar.Set(settled, l);
            }
        }

        var labels = new List<VndbListLabel>();
        foreach (var id in newLocal.Labels.Concat(newRemote.Labels).Concat(oldBase.Labels).Select(l => l.Id).Distinct())
        {
            var inLocal = newLocal.HasLabel(id);
            var agreed = inLocal == newRemote.HasLabel(id) ? inLocal : oldBase.HasLabel(id);
            if (agreed)
            {
                labels.Add(newRemote.Labels.Concat(newLocal.Labels).Concat(oldBase.Labels).First(l => l.Id == id));
            }
        }

        return LibraryListFields.Normalize(settled with { Labels = labels });
    }

    private static VndbListUpdate? BuildUpdate(LibraryListFields before, LibraryListFields after)
    {
        var setIds = after.Labels.Where(l => !before.HasLabel(l.Id)).Select(l => l.Id).ToList();
        var unsetIds = before.Labels.Where(l => !after.HasLabel(l.Id)).Select(l => l.Id).ToList();
        var update = new VndbListUpdate
        {
            Vote = Diff(before.Vote, after.Vote),
            Started = Diff(before.Started, after.Started),
            Finished = Diff(before.Finished, after.Finished),
            Notes = DiffNotes(before.Notes, after.Notes),
            LabelsSet = setIds,
            LabelsUnset = unsetIds,
        };
        return update is { Vote.Kind: VndbChangeKind.Keep, Started.Kind: VndbChangeKind.Keep, Finished.Kind: VndbChangeKind.Keep, Notes.Kind: VndbChangeKind.Keep, LabelsSet.Count: 0, LabelsUnset.Count: 0 }
            ? null
            : update;
    }

    private static VndbChange<string> DiffNotes(string? before, string? after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return default;
        }

        return after is null ? VndbChange<string>.Clear : VndbChange<string>.Set(after);
    }

    private static VndbChange<T> Diff<T>(T? before, T? after)
        where T : struct
    {
        if (Equals(before, after))
        {
            return default;
        }

        return after is { } value ? VndbChange<T>.Set(value) : VndbChange<T>.Clear;
    }

    private sealed record Scalar(string Name, Func<LibraryListFields, object?> Get, Func<LibraryListFields, object?, LibraryListFields> Set, Func<object?, string> Show, LibrarySyncFields Group);
}
