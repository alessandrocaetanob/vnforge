using System.CommandLine;
using System.Globalization;
using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using static VNForge.Cli.AiCommandSupport;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf translate --batch</c>: a tradução noturna. Manda o escopo pela Message Batches API (metade do preço) e guarda o id do lote em
/// <c>.cache/lote-noturno/</c>. Rodar o mesmo comando de novo mostra o andamento e, quando o lote terminou, grava o resultado com um ponto de restauração
/// (o "Desfazer" fica na lista de tarefas do app).
/// </summary>
internal static partial class TranslateCommand
{
    private static readonly Option<bool> BatchOption = new("--batch")
    {
        Description = "Manda as linhas pela Message Batches API, com 50% de desconto. O resultado volta em até 24 h: rode o comando de novo para ver o andamento e gravar quando chegar.",
    };

    private static readonly Option<bool> CancelBatchOption = new("--cancel-batch")
    {
        Description = "Cancela o lote noturno em aberto do projeto. O que já tinha voltado é gravado quando a API encerra o lote.",
    };

    private static void AddBatchOptions(Command command)
    {
        command.Options.Add(BatchOption);
        command.Options.Add(CancelBatchOption);
    }

    private static bool WantsBatch(ParseResult parseResult) => parseResult.GetValue(BatchOption) || parseResult.GetValue(CancelBatchOption);

    private sealed record BatchConsole(TextWriter Output, TextWriter Error, bool Json);

    private sealed record BatchScope(
        AiRunSettings Settings,
        System.Text.RegularExpressions.Regex? Filter,
        int? Limit,
        HashSet<TranslationStatus> Statuses,
        LineSelection Selection,
        TranslationBatchBuilder Builder,
        TranslationCache Cache);

    private static async Task<int> RunBatchAsync(
        VnfCli cli,
        ParseResult parseResult,
        BatchConsole console,
        ProjectFolder project,
        BatchScope scope,
        CancellationToken token)
    {
        var (output, error, json) = console;
        var (settings, filter, limit, statuses, selection, builder, cache) = scope;
        var open = new NightBatchStore(project).Open();
        bool Wanted(TextUnit u) => statuses.Contains(u.Status) && !string.IsNullOrWhiteSpace(u.Source);
        IMessageBatchClient Client() => cli.MessageBatchClientFactory?.Invoke() ?? ClaudeProviderFactory.CreateBatchClient(RequireApiKey(cli));

        if (parseResult.GetValue(CancelBatchOption))
        {
            return await CancelOpenAsync(output, json, open, new LazyBatchClient(Client), project, token).ConfigureAwait(false);
        }

        if (parseResult.GetValue(Common.DryRun))
        {
            return WriteDryRun(output, json, settings, selection, builder, cache, batch: true);
        }

        var service = new NightBatchService(new LazyBatchClient(Client), project);
        if (open.Count > 0)
        {
            return await CheckOpenAsync(output, json, open, service, token).ConfigureAwait(false);
        }

        var pending = selection.Pending;
        var budget = ReadBudget(parseResult, cli, error);
        var summary = new RunSummary { Budget = budget };
        if (pending.Count == 0)
        {
            return WriteResult(output, json, settings, 0, summary);
        }

        var tracker = new RunCostTracker(project, settings.Model, budget.Rate, static () => null);
        var estimator = new RequestEstimator(selection, builder, settings.Model, settings.Effort, settings.BatchSize, budget.Rate, cache);

        // O preço do lote é a metade do pedido normal; o valor de aviso e o limite de gasto valem sobre essa metade.
        var asker = tracker.CreateGuard(
            budget.Limits,
            lines => NightBatchService.PriceBrl(estimator.Brl(lines)),
            (question, _) => Task.FromResult(ConfirmationPrompt.Confirm(parseResult, cli, AskText(question))));
        if (await asker.AuthorizeAsync(asker.EstimateRequest(pending), token).ConfigureAwait(false) is { } refusal)
        {
            summary.Stop = refusal;
            return WriteResult(output, json, settings, pending.Count, summary);
        }

        var order = new NightBatchOrder(
            filter is null ? "O jogo todo" : "Scripts escolhidos",
            settings.Model,
            settings.Effort,
            settings.BatchSize,
            budget.Rate,
            ScriptFilter(filter),
            Wanted,
            limit);
        var state = await service.SubmitAsync(order, token).ConfigureAwait(false);
        return state is null ? WriteResult(output, json, settings, 0, summary) : WriteSubmitted(output, json, settings, state);
    }

    private static int WriteSubmitted(TextWriter output, bool json, AiRunSettings settings, NightBatchState state)
    {
        if (json)
        {
            var data = JsonBase(settings, dryRun: false, state.LineCount);
            data["batch"] = BatchJson(state);
            output.WriteLine(JsonEnvelope.Result(data));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Lote enviado: {state.Id} · {Count(state.LineCount, "linha", "linhas")} · {state.Model} · cerca de {Brl(state.EstimatedBrl)}, metade do preço de traduzir agora.");
        output.WriteLine("O resultado volta em até 24 h e a API o guarda por 29 dias. O id fica em .cache/lote-noturno/, então pode fechar o terminal.");
        output.WriteLine("Rode 'vnf translate --batch' de novo para ver o andamento e gravar quando chegar.");
        return ErrorHandler.OkExitCode;
    }

    private static async Task<int> CheckOpenAsync(TextWriter output, bool json, IReadOnlyList<NightBatchState> open, NightBatchService service, CancellationToken token)
    {
        var checks = new List<NightBatchCheck>();
        foreach (var state in open)
        {
            checks.Add(await service.CheckAsync(state.Id, token).ConfigureAwait(false));
        }

        var anyFailed = checks.Any(c => c.JustApplied && c.State.Outcome is { Failed: > 0 } or { Refused: > 0 });
        var exitCode = anyFailed ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new Dictionary<string, object?> { ["batches"] = checks.Select(c => BatchJson(c.State, c.Info)) }, exitCode));
            return exitCode;
        }

        foreach (var check in checks)
        {
            WriteCheck(output, check);
        }

        return exitCode;
    }

    private static void WriteCheck(TextWriter output, NightBatchCheck check)
    {
        var state = check.State;
        if (check.JustApplied && state.Outcome is { } outcome)
        {
            output.WriteLine($"Lote {state.Id} ({state.Title}) chegou e foi gravado: {Count(outcome.Translated, "linha traduzida", "linhas traduzidas")} (status: machine), modelo {state.Model}, custo {Dollars(outcome.Dollars)}.");
            if (outcome.Skipped > 0)
            {
                output.WriteLine($"{Count(outcome.Skipped, "linha foi mudada", "linhas foram mudadas")} durante a espera e ficou como a deixaram; a tradução do lote não a sobrescreveu.");
            }

            if (outcome.Failed > 0)
            {
                output.WriteLine($"{Count(outcome.Failed, "linha voltou", "linhas voltaram")} com erro e segue sem tradução; rode 'vnf translate' para repetir só o que falta.");
            }

            if (outcome.Refused > 0)
            {
                output.WriteLine($"{Count(outcome.Refused, "linha foi recusada", "linhas foram recusadas")} pelo modelo ou fora das regras de validação; seguem sem tradução.");
            }

            if (outcome.RestorePointId is not null)
            {
                output.WriteLine("O Desfazer do lote está na lista de tarefas do app, por 30 dias.");
            }

            return;
        }

        var info = check.Info;
        var sent = state.SubmittedAt.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
        var progress = info is { Total: > 0 } ? $", {Number(info.Done)} de {Count(info.Total, "pedido pronto", "pedidos")}" : string.Empty;
        var phase = state.Phase == NightBatchPhase.Canceling ? "cancelando" : "em andamento";
        output.WriteLine($"Lote {state.Id} ({state.Title}): {phase}{progress}. Enviado em {sent}, {Count(state.LineCount, "linha", "linhas")}, cerca de {Brl(state.EstimatedBrl)}.");
        output.WriteLine("A API leva até 24 h. Rode 'vnf translate --batch' de novo mais tarde; use 'vnf translate --cancel-batch' para cancelar.");
    }

    private static async Task<int> CancelOpenAsync(TextWriter output, bool json, IReadOnlyList<NightBatchState> open, IMessageBatchClient client, ProjectFolder project, CancellationToken token)
    {
        var service = new NightBatchService(client, project);
        var canceled = new List<NightBatchState>();
        foreach (var state in open)
        {
            canceled.Add(await service.CancelAsync(state.Id, token).ConfigureAwait(false));
        }

        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new Dictionary<string, object?> { ["batches"] = canceled.Select(s => BatchJson(s)) }));
            return ErrorHandler.OkExitCode;
        }

        if (canceled.Count == 0)
        {
            await output.WriteLineAsync("Nenhum lote em aberto neste projeto.");
            return ErrorHandler.OkExitCode;
        }

        foreach (var state in canceled)
        {
            await output.WriteLineAsync($"Cancelamento pedido para o lote {state.Id} ({state.Title}). O que já tinha voltado é gravado quando a API encerrar o lote: rode 'vnf translate --batch' mais tarde.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static object BatchJson(NightBatchState state, MessageBatchInfo? info = null) => new
    {
        id = state.Id,
        title = state.Title,
        model = state.Model,
        status = state.Phase switch
        {
            NightBatchPhase.Applied => "applied",
            NightBatchPhase.Canceling => "canceling",
            _ => "processing",
        },
        lines = state.LineCount,
        estimatedBrl = state.EstimatedBrl,
        submittedAt = state.SubmittedAt,
        requests = info is null ? null : new { total = info.Total, done = info.Done },
        outcome = state.Outcome is not { } o ? null : new { translated = o.Translated, failed = o.Failed, refused = o.Refused, skipped = o.Skipped, dollars = o.Dollars },
    };

    /// <summary>Cria o cliente só na primeira chamada à API: o envio de um lote pede a chave, mas a estimativa e o "nada a traduzir" não.</summary>
    private sealed class LazyBatchClient(Func<IMessageBatchClient> create) : IMessageBatchClient
    {
        private IMessageBatchClient? client;

        private IMessageBatchClient Client => client ??= create();

        public Task<MessageBatchInfo> CreateAsync(IReadOnlyList<MessageBatchItem> items, CancellationToken ct) => Client.CreateAsync(items, ct);

        public Task<MessageBatchInfo> GetAsync(string batchId, CancellationToken ct) => Client.GetAsync(batchId, ct);

        public Task<MessageBatchInfo> CancelAsync(string batchId, CancellationToken ct) => Client.CancelAsync(batchId, ct);

        public IAsyncEnumerable<MessageBatchEntry> ResultsAsync(string batchId, CancellationToken ct) => Client.ResultsAsync(batchId, ct);
    }
}
