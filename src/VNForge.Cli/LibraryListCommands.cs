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
        Description = "Depois de gravar aqui, envia este jogo ao VNDB (precisa do token com listwrite).",
    };

    public static Command CreateSync(VnfCli cli)
    {
        var command = new Command("sync", "Sincroniza rótulos, voto, datas e notas com a sua lista do VNDB, nos dois sentidos.");
        command.Options.Add(PullOption);
        command.Options.Add(PushOption);
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
        output.WriteLine("    Sincronização: " + (state.SyncedAt is null ? "nunca sincronizado" : state.Pending ? "há mudança aqui que o VNDB ainda não tem" : "em dia") + (state.SyncedAt is { } at ? $" (última em {at.LocalDateTime.ToString("g", PortugueseBrazil)})" : string.Empty));
    }

    private static string DateText(DateOnly? date) => date is { } value ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "nenhuma";

    private static async Task<int> SyncAsync(VnfCli cli, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var pull = parseResult.GetValue(PullOption);
        var push = parseResult.GetValue(PushOption);
        var dryRun = parseResult.GetValue(DryRunOption);
        if (pull && push)
        {
            throw new UsageException("Use --pull ou --push, não os dois: sem nenhum dos dois a sincronização anda nos dois sentidos.");
        }

        var direction = pull ? LibrarySyncDirection.Pull : push ? LibrarySyncDirection.Push : LibrarySyncDirection.Both;
        var json = parseResult.GetValue(cli.Options.Json);
        var output = parseResult.InvocationConfiguration.Output;
        var token = RequireToken(cli);

        var file = new LibraryFile(cli.UserConfigDirectory);
        var library = file.Load();
        var synchronizer = new LibraryListSynchronizer(cli.CreateVndbUserList());
        var plan = await synchronizer.PlanAsync(library, token, new LibrarySyncOptions { Direction = direction }, cancellationToken).ConfigureAwait(false);

        if (dryRun)
        {
            WriteResult(output, json, new LibrarySyncReport(plan.Account, plan.Items, plan.RemoteOnly, false, null), direction, dryRun: true);
            return ErrorHandler.OkExitCode;
        }

        if (plan.PushCount > 0
            && !ConfirmationPrompt.Confirm(parseResult, cli, $"Enviar as mudanças de {CliFormat.Count(plan.PushCount, "jogo", "jogos")} para a lista de {plan.Account.Username} no VNDB?"))
        {
            output.WriteLine("Operação cancelada. Nada foi enviado nem gravado.");
            return ErrorHandler.CancelledExitCode;
        }

        LibrarySyncReport report;
        try
        {
            report = await synchronizer.ApplyAsync(library, plan, token, json ? null : new SyncLines(parseResult.InvocationConfiguration.Error), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            file.Save(library);
        }

        var exitCode = report.Items.Any(i => i.Action == LibrarySyncAction.Failed) || report.AbortReason is not null ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        WriteResult(output, json, report, direction, dryRun: false, exitCode);
        cancellationToken.ThrowIfCancellationRequested();
        return exitCode;
    }

    private static async Task<int> StatusAsync(VnfCli cli, ParseResult parseResult, string reference, CancellationToken cancellationToken)
    {
        var json = parseResult.GetValue(cli.Options.Json);
        var output = parseResult.InvocationConfiguration.Output;
        var file = new LibraryFile(cli.UserConfigDirectory);
        var library = file.Load();
        var game = library.Find(reference);
        if (!game.IsConfirmed)
        {
            throw new UsageException($"'{game.Title}' não tem jogo do VNDB confirmado; confirme com vnf library match --confirm {game.Id} <vndb-id> antes de mexer na lista.");
        }

        var update = ReadUpdate(parseResult);
        var edited = update is not null;
        if (update is not null)
        {
            try
            {
                var state = LibraryListState.Edit(game.CurrentList, game.VndbId!, update, TimeProvider.System.GetUtcNow());
                game = game with { List = state };
            }
            catch (ArgumentException e)
            {
                throw new UsageException(e.Message);
            }

            library.Replace(game);
            file.Save(library);
        }

        LibrarySyncItem? sent = null;
        string? sendNote = null;
        if (parseResult.GetValue(PushNowOption))
        {
            if (game.CurrentList is null)
            {
                throw new UsageException("Este jogo ainda não tem nada na lista para enviar. Mude algo com --label, --vote, --started, --finished ou --notes.");
            }

            var token = RequireToken(cli);
            var synchronizer = new LibraryListSynchronizer(cli.CreateVndbUserList());
            var plan = await synchronizer.PlanAsync(library, token, new LibrarySyncOptions { Direction = LibrarySyncDirection.Push, GameId = game.Id }, cancellationToken).ConfigureAwait(false);
            LibrarySyncReport report;
            try
            {
                report = await synchronizer.ApplyAsync(library, plan, token, null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                file.Save(library);
            }

            sent = report.Items.FirstOrDefault();
            sendNote = report.AbortReason ?? sent?.Error;
            game = library.Find(game.Id);
        }

        var failed = sendNote is not null;
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(
                new
                {
                    game = new { id = game.Id, title = game.Title, vndbId = game.VndbId },
                    list = Data(game),
                    edited,
                    sent = sent is null ? null : new { action = ActionName(sent.Action), pushed = sent.Pushed, conflicts = ConflictData(sent), error = sendNote },
                },
                failed ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode));
            return failed ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        }

        output.WriteLine($"{game.Title} ({game.VndbId})");
        WriteLines(output, game);
        if (sent is not null)
        {
            WriteSent(output, sent, sendNote);
        }
        else if (edited && game.CurrentList is { Pending: true })
        {
            output.WriteLine("Gravado só aqui. Para enviar ao VNDB: vnf library sync --push (ou repita este comando com --push).");
        }

        return failed ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
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
            Vote = vote is null ? default : vote.Trim().Length == 0 ? VndbChange<int>.Clear : ParseVote(vote),
            Started = ReadDate(started, "--started"),
            Finished = ReadDate(finished, "--finished"),
            Notes = notes is null ? default : notes.Trim().Length == 0 ? VndbChange<string>.Clear : VndbChange<string>.Set(notes.Trim()),
        };
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
                throw new UsageException($"{option}: rótulo desconhecido '{value}'. Use um de: {LibraryListLabels.Names}, ou o id (8 a {LibraryListLabels.MaxId}) de um rótulo seu. O 'Voted' o VNDB põe sozinho com o voto.");
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string RequireToken(VnfCli cli) =>
        cli.ResolveVndbToken().Token
        ?? throw new UsageException($"Não há token do VNDB. Gere um em vndb.org/u/tokens e defina {VNForge.Vndb.VndbTokenResolver.EnvironmentVariable} ou rode vnf vndb token set.");

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
                    items = items.Where(i => i.Action != LibrarySyncAction.Unchanged).Select(i => new
                    {
                        game = i.Game.Id,
                        title = i.Game.Title,
                        vndbId = i.Game.VndbId,
                        action = ActionName(i.Action),
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

        foreach (var item in items.Where(i => i.Action != LibrarySyncAction.Unchanged))
        {
            if (item.Action == LibrarySyncAction.Failed)
            {
                output.WriteLine($"  Falhou: {item.Game.Title}: {item.Error}");
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

            output.WriteLine($"  {item.Game.Title}: {string.Join("; ", parts)}");
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
