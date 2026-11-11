using System.CommandLine;
using System.Globalization;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Vndb;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf library sync</c> e <c>vnf library status</c>: a lista do VNDB (rótulos, voto, datas, notas) de cada jogo da biblioteca. <c>status</c> edita só o que a
/// biblioteca guarda; nada vai para o VNDB sem <c>sync</c> (ou <c>status --push</c>). O token vem de <c>VNDB_TOKEN</c> ou do cofre e nunca sai em mensagem nem em <c>--json</c>.
/// </summary>
internal static class LibraryListCommands
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Option<bool> PullOption = new("--pull")
    {
        Description = "Só traz do VNDB; não envia nada. Nos conflitos, vale o VNDB.",
    };

    private static readonly Option<bool> PushOption = new("--push")
    {
        Description = "Só envia ao VNDB o que mudou aqui; não traz nada. Nos conflitos, vale o que está aqui.",
    };

    private static readonly Option<bool> RecreateOption = new("--recreate")
    {
        Description = "Recria no VNDB a entrada que você apagou no site e que tem mudança aqui; sem mudança aqui ela não é recriada. Sem esta opção nenhuma é recriada. Não vale com --pull.",
    };

    private static readonly Option<bool> DryRunOption = new("--dry-run")
    {
        Description = "Mostra o que seria trazido e enviado, sem gravar nada, nem aqui nem no VNDB.",
    };

    private static readonly Option<string[]> LabelOption = new("--label")
    {
        Description = $"Põe um rótulo no jogo: {LibraryListLabels.Names} ou o id de um rótulo seu. Pode repetir.",
        HelpName = "rótulo",
    };

    private static readonly Option<string[]> UnlabelOption = new("--unlabel")
    {
        Description = "Tira um rótulo do jogo. Pode repetir.",
        HelpName = "rótulo",
    };

    private static readonly Option<string?> VoteOption = new("--vote")
    {
        Description = "A nota de 1 a 10, com no máximo uma casa decimal (8.5). Vazia (\"\") apaga o voto.",
        HelpName = "nota",
    };

    private static readonly Option<string?> StartedOption = new("--started")
    {
        Description = "A data em que começou, AAAA-MM-DD. Vazia (\"\") apaga.",
        HelpName = "data",
    };

    private static readonly Option<string?> FinishedOption = new("--finished")
    {
        Description = "A data em que terminou, AAAA-MM-DD. Vazia (\"\") apaga.",
        HelpName = "data",
    };

    private static readonly Option<string?> NotesOption = new("--notes")
    {
        Description = "As suas notas sobre o jogo. Vazia (\"\") apaga.",
        HelpName = "texto",
    };

    private static readonly Option<bool> PushNowOption = new("--push")
    {
        Description = "Depois de gravar aqui, envia este jogo ao VNDB, perguntando antes (--yes pula; com --json é obrigatório). Precisa do token com listwrite.",
    };

    public static Command CreateSync(VnfCli cli)
    {
        var command = new Command("sync", "Sincroniza rótulos, voto, datas e notas com a sua lista do VNDB, nos dois sentidos.");
        command.Options.Add(PullOption);
        command.Options.Add(PushOption);
        command.Options.Add(RecreateOption);
        command.Options.Add(DryRunOption);
        command.SetAction((parseResult, token) => SyncAsync(cli, parseResult, token));
        return command;
    }

    public static Command CreateStatus(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("status", "Mostra ou muda a lista do VNDB de um jogo: rótulos, voto, datas e notas.");
        command.Arguments.Add(game);
        command.Options.Add(LabelOption);
        command.Options.Add(UnlabelOption);
        command.Options.Add(VoteOption);
        command.Options.Add(StartedOption);
        command.Options.Add(FinishedOption);
        command.Options.Add(NotesOption);
        command.Options.Add(PushNowOption);
        command.SetAction((parseResult, token) => StatusAsync(cli, parseResult, parseResult.GetRequiredValue(game), token));
        return command;
    }

    /// <summary>A lista do jogo para o <c>--json</c>: nulo se o jogo ainda não tem estado de lista.</summary>
    public static object? Data(LibraryGame game)
    {
        if (game.CurrentList is not { } state)
        {
            return null;
        }

        return new
        {
            labels = state.Fields.Labels.Select(l => new { id = l.Id, label = l.Label, name = LibraryListLabels.Display(l) }),
            vote = state.Fields.Vote,
            started = state.Fields.Started,
            finished = state.Fields.Finished,
            notes = state.Fields.Notes,
            pending = state.Pending,
            modifiedAt = state.ModifiedAt,
            syncedAt = state.SyncedAt,
        };
    }

    public static void WriteLines(TextWriter output, LibraryGame game)
    {
        if (game.CurrentList is not { } state)
        {
            if (game.IsConfirmed)
            {
                output.WriteLine("  Lista do VNDB: nada ainda (vnf library status <jogo> --label ..., ou vnf library sync).");
            }

            return;
        }

        var fields = state.Fields;
        output.WriteLine("  Lista do VNDB:");
        output.WriteLine($"    Rótulos: {(fields.Labels.Count == 0 ? "nenhum" : string.Join(", ", fields.Labels.Select(LibraryListLabels.Display)))}");
        output.WriteLine($"    Voto: {(fields.Vote is { } vote ? LibraryListRules.FormatVote(vote) : "nenhum")}");
        output.WriteLine($"    Início: {DateText(fields.Started)}");
        output.WriteLine($"    Fim: {DateText(fields.Finished)}");
        output.WriteLine($"    Notas: {fields.Notes ?? "nenhuma"}");
        output.WriteLine("    Sincronização: " + SyncStatus(state) + (state.SyncedAt is { } at ? $" (última em {at.LocalDateTime.ToString("g", PortugueseBrazil)})" : string.Empty));
    }

    private static string SyncStatus(LibraryListState state)
    {
        if (state.SyncedAt is null)
        {
            return "nunca sincronizado";
        }

        return state.Pending ? "há mudança aqui que o VNDB ainda não tem" : "em dia";
    }

    private static string DateText(DateOnly? date) => date is { } value ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "nenhuma";

    private static LibrarySyncDirection SyncDirection(bool pull, bool push)
    {
        if (pull)
        {
            return LibrarySyncDirection.Pull;
        }

        return push ? LibrarySyncDirection.Push : LibrarySyncDirection.Both;
    }

    private static async Task<int> SyncAsync(VnfCli cli, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var pull = parseResult.GetValue(PullOption);
        var push = parseResult.GetValue(PushOption);
        var dryRun = parseResult.GetValue(DryRunOption);
        if (pull && push)
        {
            throw new UsageException("Use --pull ou --push, não os dois: sem nenhum dos dois a sincronização anda nos dois sentidos.");
        }

        var recreate = parseResult.GetValue(RecreateOption);
        if (recreate && pull)
        {
            throw new UsageException("--recreate manda entradas ao VNDB e não combina com --pull.");
        }

        var direction = SyncDirection(pull, push);
        var json = parseResult.GetValue(cli.Options.Json);
        var output = parseResult.InvocationConfiguration.Output;
        var token = RequireToken(cli);

        var file = new LibraryFile(cli.UserConfigDirectory);
        var library = file.Load();
        var synchronizer = new LibraryListSynchronizer(cli.CreateVndbUserList());
        var plan = await synchronizer.PlanAsync(library, token, new LibrarySyncOptions { Direction = direction, RecreateRemoved = recreate }, cancellationToken).ConfigureAwait(false);

        if (dryRun)
        {
            WriteResult(output, json, new LibrarySyncReport(plan.Account, plan.Items, plan.RemoteOnly, false, null), direction, dryRun: true);
            return ErrorHandler.OkExitCode;
        }

        if (plan.PushCount > 0
            && !ConfirmationPrompt.Confirm(parseResult, cli, $"Enviar as mudanças de {CliFormat.Count(plan.PushCount, "jogo", "jogos")} para a lista de {plan.Account.Username} no VNDB?"))
        {
            await output.WriteLineAsync("Operação cancelada. Nada foi enviado nem gravado.");
            return ErrorHandler.CancelledExitCode;
        }

        var report = await synchronizer.ApplyAsync(plan, token, (game, state) => Keep(file, game, state), json ? null : new SyncLines(parseResult.InvocationConfiguration.Error), cancellationToken).ConfigureAwait(false);
        var exitCode = report.Items.Any(i => i.Action == LibrarySyncAction.Failed) || report.AbortReason is not null ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        WriteResult(output, json, report, direction, dryRun: false, exitCode);
        cancellationToken.ThrowIfCancellationRequested();
        return exitCode;
    }

    private static async Task<int> StatusAsync(VnfCli cli, ParseResult parseResult, string reference, CancellationToken cancellationToken)
    {
        var json = parseResult.GetValue(cli.Options.Json);
        var output = parseResult.InvocationConfiguration.Output;
        var pushNow = parseResult.GetValue(PushNowOption);
        if (pushNow && json && !parseResult.GetValue(cli.Options.Yes))
        {
            throw new UsageException("Com --json não há como perguntar: confirme o envio com --yes.");
        }

        var file = new LibraryFile(cli.UserConfigDirectory);
        var update = ReadUpdate(parseResult);
        var edited = update is not null;
        LibraryGame game;
        if (update is null)
        {
            game = ConfirmedGame(file.Load(), reference);
        }
        else
        {
            game = file.Update(current =>
            {
                var found = ConfirmedGame(current, reference);
                var changed = found with { List = Edit(found, update) };
                current.Replace(changed);
                return changed;
            });
        }

        LibrarySyncItem? sent = null;
        string? sendNote = null;
        if (pushNow)
        {
            if (game.CurrentList is null)
            {
                throw new UsageException("Este jogo ainda não tem nada na lista para enviar. Mude algo com --label, --vote, --started, --finished ou --notes.");
            }

            var token = RequireToken(cli);
            var synchronizer = new LibraryListSynchronizer(cli.CreateVndbUserList());
            var plan = await synchronizer.PlanAsync(file.Load(), token, new LibrarySyncOptions { Direction = LibrarySyncDirection.Push, GameId = game.Id }, cancellationToken).ConfigureAwait(false);
            if (plan.Items.FirstOrDefault(i => i.RemovedOnSite) is { } removed)
            {
                throw new UsageException(removed.HasLocalEdits
                    ? $"A entrada de '{game.Title}' foi apagada na sua lista do VNDB e o jogo tem mudanças aqui. A edição ficou gravada, mas não foi enviada, para não recriar o que você apagou no site. Para recriar: vnf library sync --push --recreate."
                    : $"A entrada de '{game.Title}' foi apagada na sua lista do VNDB e não há mudança aqui para enviar. Para trazer a exclusão para a biblioteca: vnf library sync.");
            }

            if (plan.PushCount > 0 && !ConfirmationPrompt.Confirm(parseResult, cli, $"Enviar as mudanças de '{game.Title}' para a lista de {plan.Account.Username} no VNDB?"))
            {
                await output.WriteLineAsync("Operação cancelada. Nada foi enviado; a edição ficou gravada aqui.");
                return ErrorHandler.CancelledExitCode;
            }

            var report = await synchronizer.ApplyAsync(plan, token, (planned, state) => Keep(file, planned, state), null, cancellationToken).ConfigureAwait(false);
            sent = report.Items.Count > 0 ? report.Items[0] : null;
            sendNote = report.AbortReason ?? sent?.Error;
            game = file.Load().Find(game.Id);
        }

        var failed = sendNote is not null;
        var exitCode = failed ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        var warning = edited ? LabelWarning(game) : null;
        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(
                new
                {
                    game = new { id = game.Id, title = game.Title, vndbId = game.VndbId },
                    list = Data(game),
                    edited,
                    warning,
                    sent = sent is null ? null : new { action = ActionName(sent.Action), removedOnSite = sent.RemovedOnSite, hasLocalEdits = sent.HasLocalEdits, pushed = sent.Pushed, conflicts = ConflictData(sent), error = sendNote },
                },
                exitCode));
            return exitCode;
        }

        await output.WriteLineAsync($"{game.Title} ({game.VndbId})");
        WriteLines(output, game);
        if (warning is not null)
        {
            await output.WriteLineAsync("Aviso: " + warning);
        }

        if (sent is not null)
        {
            WriteSent(output, sent, sendNote);
        }
        else if (edited && game.CurrentList is { Pending: true })
        {
            await output.WriteLineAsync("Gravado só aqui. Para enviar ao VNDB: vnf library sync --push (ou repita este comando com --push).");
        }

        return exitCode;
    }

    private static LibraryGame ConfirmedGame(GameLibrary library, string reference)
    {
        var game = library.Find(reference);
        return game.IsConfirmed
            ? game
            : throw new UsageException($"'{game.Title}' não tem jogo do VNDB confirmado; confirme com vnf library match --confirm {game.Id} <vndb-id> antes de mexer na lista.");
    }

    private static LibraryListState Edit(LibraryGame game, VndbListUpdate update)
    {
        try
        {
            return LibraryListState.Edit(game.CurrentList, game.VndbId!, update, TimeProvider.System.GetUtcNow());
        }
        catch (ArgumentException e)
        {
            throw new UsageException(e.Message);
        }
    }

    /// <summary>Grava o estado novo relendo a biblioteca sob a trava do arquivo: só o <c>List</c> do jogo muda, o resto vem do que está no arquivo agora.</summary>
    private static bool Keep(LibraryFile file, LibraryGame planned, LibraryListState state) =>
        file.Update(current => LibraryListSynchronizer.Keep(current, planned, state));

    /// <summary>O app não tira um rótulo ao pôr outro: o jogo pode ficar Jogando e Terminado ao mesmo tempo, e a pessoa decide.</summary>
    private static string? LabelWarning(LibraryGame game)
    {
        var fields = game.CurrentList?.Fields;
        return fields is not null && fields.HasLabel(LibraryListLabels.Playing) && (fields.HasLabel(LibraryListLabels.Finished) || fields.HasLabel(LibraryListLabels.Stalled) || fields.HasLabel(LibraryListLabels.Dropped))
            ? "o jogo está como Jogando e também como Terminado, Parado ou Abandonado. O app não tira um rótulo ao pôr outro; use --unlabel jogando se não for isso que você quer."
            : null;
    }

    private static void WriteSent(TextWriter output, LibrarySyncItem sent, string? error)
    {
        if (error is not null)
        {
            output.WriteLine($"Não foi possível enviar ao VNDB: {error}");
            return;
        }

        output.WriteLine(sent.Pushed.Count == 0 ? "O VNDB já estava igual; nada foi enviado." : $"Enviado ao VNDB: {string.Join(", ", sent.Pushed)}.");
        foreach (var conflict in sent.Conflicts)
        {
            output.WriteLine("  " + ConflictText(sent.Game, conflict));
        }
    }

    private static VndbListUpdate? ReadUpdate(ParseResult parseResult)
    {
        var labels = ReadLabels(parseResult.GetValue(LabelOption), "--label");
        var unlabels = ReadLabels(parseResult.GetValue(UnlabelOption), "--unlabel");
        var vote = parseResult.GetValue(VoteOption);
        var started = parseResult.GetValue(StartedOption);
        var finished = parseResult.GetValue(FinishedOption);
        var notes = parseResult.GetValue(NotesOption);
        if (labels.Count == 0 && unlabels.Count == 0 && vote is null && started is null && finished is null && notes is null)
        {
            return null;
        }

        if (labels.Intersect(unlabels).Any())
        {
            throw new UsageException("O mesmo rótulo não pode entrar em --label e em --unlabel.");
        }

        return new VndbListUpdate
        {
            LabelsSet = labels,
            LabelsUnset = unlabels,
            Vote = ReadVote(vote),
            Started = ReadDate(started, "--started"),
            Finished = ReadDate(finished, "--finished"),
            Notes = ReadNotes(notes),
        };
    }

    private static VndbChange<int> ReadVote(string? vote)
    {
        if (vote is null)
        {
            return default;
        }

        return vote.Trim().Length == 0 ? VndbChange<int>.Clear : ParseVote(vote);
    }

    private static VndbChange<string> ReadNotes(string? notes)
    {
        if (notes is null)
        {
            return default;
        }

        return notes.Trim().Length == 0 ? VndbChange<string>.Clear : VndbChange<string>.Set(notes.Trim());
    }

    private static VndbChange<int> ParseVote(string text)
    {
        try
        {
            return VndbChange<int>.Set(LibraryListRules.ParseVote(text));
        }
        catch (ArgumentException e)
        {
            throw new UsageException(e.Message);
        }
    }

    private static VndbChange<DateOnly> ReadDate(string? text, string option)
    {
        if (text is null)
        {
            return default;
        }

        if (text.Trim().Length == 0)
        {
            return VndbChange<DateOnly>.Clear;
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? VndbChange<DateOnly>.Set(date)
            : throw new UsageException($"{option} quer uma data no formato AAAA-MM-DD, não '{text}'.");
    }

    private static List<int> ReadLabels(string[]? values, string option)
    {
        var ids = new List<int>();
        foreach (var value in values ?? [])
        {
            if (!LibraryListLabels.TryParse(value, out var id))
            {
                throw new UsageException($"{option}: rótulo desconhecido '{value}'. Use um de: {LibraryListLabels.Names}, ou o id ({LibraryListLabels.FirstAccountId} a {LibraryListLabels.MaxId}) de um rótulo seu. O 'Voted' o VNDB põe sozinho com o voto.");
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string RequireToken(VnfCli cli)
    {
        var resolved = cli.ResolveVndbToken();
        return resolved.Token
            ?? throw new UsageException($"Não há token do VNDB. Gere um em vndb.org/u/tokens e defina {VNForge.Vndb.VndbTokenResolver.EnvironmentVariable} ou rode vnf vndb token set.{(resolved.Notice is null ? string.Empty : " " + resolved.Notice)}");
    }

    private static void WriteResult(TextWriter output, bool json, LibrarySyncReport report, LibrarySyncDirection direction, bool dryRun, int exitCode = ErrorHandler.OkExitCode)
    {
        var items = report.Items;
        int Of(LibrarySyncAction action) => items.Count(i => i.Action == action);
        var pulledGames = items.Count(i => i.Pulled.Count > 0);
        var pushedGames = items.Count(i => i.Pushed.Count > 0);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(
                new
                {
                    dryRun,
                    direction = direction.ToString().ToLowerInvariant(),
                    account = new { userId = report.Account.UserId, username = report.Account.Username },
                    games = items.Count,
                    pulled = pulledGames,
                    pushed = pushedGames,
                    unchanged = Of(LibrarySyncAction.Unchanged),
                    failed = Of(LibrarySyncAction.Failed),
                    remoteOnly = report.RemoteOnly,
                    cancelled = report.Cancelled,
                    abortReason = report.AbortReason,
                    items = items.Where(i => i.Action != LibrarySyncAction.Unchanged || i.RemovedOnSite).Select(i => new
                    {
                        game = i.Game.Id,
                        title = i.Game.Title,
                        vndbId = i.Game.VndbId,
                        action = ActionName(i.Action),
                        removedOnSite = i.RemovedOnSite,
                        hasLocalEdits = i.HasLocalEdits,
                        pulled = i.Pulled,
                        pushed = i.Pushed,
                        conflicts = ConflictData(i),
                        error = i.Error,
                    }),
                },
                exitCode));
            return;
        }

        output.WriteLine($"Conta {report.Account.Username} ({report.Account.UserId}){(dryRun ? ", simulação: nada foi gravado" : string.Empty)}.");
        if (report.Cancelled)
        {
            output.WriteLine("Sincronização cancelada. O que já foi feito ficou gravado; rode de novo para continuar.");
        }

        foreach (var item in items.Where(i => i.Action != LibrarySyncAction.Unchanged || i.RemovedOnSite))
        {
            if (item.Action == LibrarySyncAction.Failed)
            {
                output.WriteLine($"  Falhou: {item.Game.Title}: {item.Error}");
                continue;
            }

            if (item.Action == LibrarySyncAction.Unchanged)
            {
                output.WriteLine(item.HasLocalEdits
                    ? $"  Aviso: {item.Game.Title} foi apagado da sua lista do VNDB e tem mudanças aqui; não foi recriado. Para recriar: vnf library sync --recreate."
                    : $"  Aviso: {item.Game.Title} foi apagado da sua lista do VNDB e não há mudança aqui; --push não traz a exclusão. Para trazê-la: vnf library sync.");
                continue;
            }

            var parts = new List<string>();
            if (item.Pulled.Count > 0)
            {
                parts.Add($"{(dryRun ? "traria" : "trouxe")} {string.Join(", ", item.Pulled)}");
            }

            if (item.Pushed.Count > 0)
            {
                parts.Add($"{(dryRun ? "enviaria" : "enviou")} {string.Join(", ", item.Pushed)}");
            }

            output.WriteLine($"  {item.Game.Title}: {string.Join("; ", parts)}{(item.RemovedOnSite ? " (a entrada foi apagada no site)" : string.Empty)}");
            foreach (var conflict in item.Conflicts)
            {
                output.WriteLine("    Aviso: " + ConflictText(item.Game, conflict));
            }
        }

        if (report.AbortReason is not null)
        {
            output.WriteLine(report.AbortReason);
        }

        output.WriteLine(
            $"{CliFormat.Count(items.Count, "jogo", "jogos")}: {pulledGames} {(dryRun ? "a trazer" : "trazidos")}, {pushedGames} {(dryRun ? "a enviar" : "enviados")}, "
            + $"{Of(LibrarySyncAction.Unchanged)} sem mudança, {Of(LibrarySyncAction.Failed)} com falha.");
        if (report.RemoteOnly > 0)
        {
            output.WriteLine($"{CliFormat.Count(report.RemoteOnly, "entrada da sua lista do VNDB não tem", "entradas da sua lista do VNDB não têm")} jogo confirmado na biblioteca e ficou de fora.");
        }

        if (dryRun && (pulledGames > 0 || pushedGames > 0))
        {
            output.WriteLine("Para valer: rode o mesmo comando sem --dry-run.");
        }
    }

    private static object ConflictData(LibrarySyncItem item) =>
        item.Conflicts.Select(c => new { field = c.Field, local = c.Local, remote = c.Remote, winner = c.Winner.ToString().ToLowerInvariant() });

    private static string ConflictText(LibraryGame game, LibrarySyncConflict conflict) =>
        $"{game.Title}: o campo {conflict.Field} mudou dos dois lados (aqui: {conflict.Local}; VNDB: {conflict.Remote}). Venceu {(conflict.Winner == LibrarySyncWinner.Local ? "o que está aqui" : "o VNDB")}.";

    private static string ActionName(LibrarySyncAction action) => action.ToString().ToLowerInvariant();

    private sealed class SyncLines(TextWriter error) : IProgress<LibrarySyncProgress>
    {
        public void Report(LibrarySyncProgress value) =>
            error.WriteLine($"{CliFormat.Number(value.Done + 1)}/{CliFormat.Number(value.Total)}  {value.Game.Title}");
    }
}
