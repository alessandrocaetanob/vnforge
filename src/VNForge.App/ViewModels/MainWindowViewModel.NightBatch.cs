using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string NightPrefix = "Tradução noturna · ";

    private readonly HashSet<string> watchedBatches = [];
    private readonly List<Task> batchWatchers = [];

    /// <summary>"Desfazer o lote?": o diálogo que a lista de tarefas abre antes de devolver as linhas de um lote noturno.</summary>
    public UndoBatchDialogViewModel UndoBatch { get; } = new();

    /// <summary>Monta o cliente da Message Batches API; o app passa o SDK com a chave do usuário, os testes passam o cliente falso. Sem ele, "Durante a noite" avisa que falta a chave.</summary>
    public Func<IMessageBatchClient>? MessageBatchClientFactory { get; init; }

    /// <summary>De quanto em quanto tempo o app pergunta à API se o lote terminou; os testes põem um intervalo longo e usam "Verificar agora".</summary>
    public TimeSpan NightBatchPollInterval { get; init; } = NightBatchService.PollInterval;

    /// <summary>Termina quando os lotes noturnos que o app acompanha terminaram e foram gravados.</summary>
    public Task NightBatchWatching => Task.WhenAll(batchWatchers.ToArray());

    private void HookNightBatch() => Tasks.ConfirmUndo = ConfirmBatchUndoAsync;

    private async Task StartOvernightAsync(TranslationRequest request, ProjectSummary project)
    {
        if (MessageBatchClientFactory is not { } factory)
        {
            Toasts.Show("Falta a chave da IA. Informe-a em Ajustes, na aba IA e gasto.", ToastKind.Error, key: ToastKey);
            return;
        }

        NightBatchState? state;
        try
        {
            var folder = ProjectFolder.Open(project.Root);
            var profile = QualityProfiles.For(request.Quality);
            var ai = folder.LoadConfig().Ai;
            var keys = request.ScriptKeys.ToHashSet(StringComparer.Ordinal);
            var order = new NightBatchOrder(
                ScopeTitle(request.Scope),
                profile.Model,
                ClaudeEffortText.ParseOrDefault(ai.Effort),
                ai.BatchSize,
                Settings.Rate,
                keys.Contains,
                TranslationQuoter.IsPending);
            var service = new NightBatchService(factory(), folder);
            state = await Task.Run(() => service.SubmitAsync(order, CancellationToken.None), CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var text = e is VNForgeException or IOException or UnauthorizedAccessException ? e.Message : ClaudeErrorMessages.Describe(e);
            Toasts.Show($"O lote não foi enviado: {text}", ToastKind.Error, key: ToastKey);
            return;
        }

        if (state is null)
        {
            Toasts.Show("Nenhuma linha foi enviada: não há linha sem tradução neste escopo.", ToastKind.Info, key: ToastKey);
            return;
        }

        Toasts.Show("Lote enviado. O resultado volta em até 24 h e você pode fechar o app: ele continua de onde parou quando abrir de novo.", ToastKind.Success, key: ToastKey);
        WatchNightBatch(project.Root, state);
    }

    /// <summary>Retoma, ao abrir o projeto, os lotes que o app ou o <c>vnf</c> enviaram e que ainda não foram gravados.</summary>
    private void ResumeNightBatches(string projectRoot)
    {
        if (MessageBatchClientFactory is null)
        {
            return;
        }

        try
        {
            foreach (var state in new NightBatchStore(ProjectFolder.Open(projectRoot)).Open())
            {
                WatchNightBatch(projectRoot, state);
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // Sem ler a pasta de lotes não há o que retomar; o vnf translate --batch mostra o motivo.
        }
    }

    /// <summary>Põe o lote na lista de tarefas e o acompanha: pergunta à API a cada <see cref="NightBatchPollInterval"/> e, quando termina, grava o resultado.</summary>
    private void WatchNightBatch(string projectRoot, NightBatchState initial)
    {
        if (MessageBatchClientFactory is not { } factory || !watchedBatches.Add(initial.Id))
        {
            return;
        }

        batchWatchers.Add(WatchAsync(projectRoot, initial, factory));
    }

    private async Task WatchAsync(string projectRoot, NightBatchState initial, Func<IMessageBatchClient> factory)
    {
        using var wake = new SemaphoreSlim(0, int.MaxValue);
        NightBatchCheck? last = null;
        var canceled = false;
        BackgroundTaskItem task;
        try
        {
            var service = new NightBatchService(factory(), ProjectFolder.Open(projectRoot));
            task = TaskService.Start(NightPrefix + initial.Title, async (reporter, ct) =>
            {
                reporter.Report(0);
                while (true)
                {
                    try
                    {
                        last = await service.CheckAsync(initial.Id, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        canceled = true;
                        await TryCancelBatchAsync(service, initial.Id).ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception e) when (e is not NightBatchException and not OperationCanceledException)
                    {
                        // Rede fora do ar numa verificação: o lote continua na API, então a tarefa segue esperando.
                        var message = $"Não foi possível verificar agora: {ClaudeErrorMessages.Describe(e)} Tentando de novo.";
                        TaskService.Post(() => SetDetail(initial.Id, message));
                        await WaitAsync(wake, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (last.State.Phase == NightBatchPhase.Applied)
                    {
                        reporter.Report(1);
                        return;
                    }

                    var info = last.Info;
                    reporter.Report(info?.Fraction);
                    var detail = DescribeProgress(last.State, info);
                    TaskService.Post(() => SetDetail(initial.Id, detail));
                    await WaitAsync(wake, ct).ConfigureAwait(false);
                }
            }, usesAi: true);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            Toasts.Show($"Não foi possível acompanhar o lote {initial.Id}: {e.Message}", ToastKind.Error, key: ToastKey);
            watchedBatches.Remove(initial.Id);
            return;
        }

        task.IsBatch = true;
        task.Message = DescribeSent(initial);
        task.Detail = "Verificando o lote";
        batchTasks[initial.Id] = task;
        task.CheckNowCommand = new RelayCommand(() => wake.Release());
        try
        {
            await task.Completion;
        }
        finally
        {
            watchedBatches.Remove(initial.Id);
            batchTasks.Remove(initial.Id);
        }

        // O estado final da tarefa chega por post depois do Completion e apaga a mensagem: o que a lista mostra é gravado por post também, que entra na fila depois dele.
        if (canceled)
        {
            const string CancelText = "Cancelamento pedido. O que já tinha voltado é gravado quando a API encerrar o lote.";
            TaskService.Post(() => Finish(task, CancelText));
            Toasts.Show("Cancelamento do lote pedido. O que já tinha voltado é gravado quando a API encerrar o lote.", ToastKind.Warning, key: ToastKey);
            if (new NightBatchStore(ProjectFolder.Open(projectRoot)).Find(initial.Id) is { IsOpen: true } open)
            {
                WatchNightBatch(projectRoot, open);
            }

            return;
        }

        if (last?.State.Outcome is not { } outcome)
        {
            TaskService.Post(() => Finish(task, null));
            return;
        }

        var arrived = last.State;
        var unfinished = outcome.UnfinishedIds;
        TaskService.Post(() =>
        {
            Finish(task, DescribeArrived(arrived, outcome));
            if (outcome.RestorePointId is not null)
            {
                task.Restore = new BatchRestore(projectRoot, outcome.RestorePointId, outcome.RestorePointLines);
            }

            if (unfinished.Count > 0)
            {
                task.ShowLinesLabel = unfinished.Count == 1 ? "Ver a linha" : $"Ver as {ProjectLabels.Number(unfinished.Count)} linhas";
                task.ShowLinesCommand = new RelayCommand(() => OpenLineInEditor(LineOf(unfinished[0])));
            }
        });

        Editor.Reload();
        var (text, kind) = DescribeNightOutcome(arrived, outcome);
        Toasts.Show(text, kind, key: ToastKey);
        await RefreshSummaryPageAsync();
    }

    /// <summary>Tira da tarefa o que só vale enquanto o lote espera (Verificar agora, andamento) e, se houver, põe a mensagem final.</summary>
    private static void Finish(BackgroundTaskItem task, string? message)
    {
        task.CheckNowCommand = null;
        task.Detail = string.Empty;
        if (message is not null)
        {
            task.Message = message;
        }
    }

    private readonly Dictionary<string, BackgroundTaskItem> batchTasks = [];

    private void SetDetail(string batchId, string detail)
    {
        if (batchTasks.TryGetValue(batchId, out var task) && task.IsRunning)
        {
            task.Detail = detail;
        }
    }

    private async Task WaitAsync(SemaphoreSlim wake, CancellationToken ct) => await wake.WaitAsync(NightBatchPollInterval, ct).ConfigureAwait(false);

    private static async Task TryCancelBatchAsync(NightBatchService service, string batchId)
    {
        try
        {
            await service.CancelAsync(batchId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O cancelamento não chegou à API: o estado do lote continua aberto e a próxima verificação o retoma.
        }
    }

    private async Task<bool> ConfirmBatchUndoAsync(BackgroundTaskItem item)
    {
        if (item.Restore is not { } restore)
        {
            return true;
        }

        RestoreUndoResult preview;
        try
        {
            preview = await Task.Run(() => new RestorePointStore(ProjectFolder.Open(restore.ProjectRoot)).Preview(restore.PointId), CancellationToken.None);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // O ponto não abre: o Desfazer comum mostra o motivo.
            return true;
        }

        var name = item.Title.StartsWith(NightPrefix, StringComparison.Ordinal) ? item.Title[NightPrefix.Length..] : item.Title;
        return await UndoBatch.AskAsync(name, item.FinishedAt ?? item.StartedAt, preview);
    }

    private static LineRequest LineOf(string unitId) => new(unitId.Contains(':', StringComparison.Ordinal) ? unitId[..unitId.IndexOf(':', StringComparison.Ordinal)] : unitId, unitId);

    private static string ScopeTitle(TranslationScope scope) => scope switch
    {
        TranslationScope.Scene => "Esta cena",
        TranslationScope.Chapter => "O capítulo",
        TranslationScope.Selection => "As cenas escolhidas",
        _ => "O jogo todo",
    };

    private static string QualityName(string quality) => quality switch
    {
        nameof(TranslationQuality.Fast) => "rápida",
        nameof(TranslationQuality.Balanced) => "equilibrada",
        nameof(TranslationQuality.Careful) => "caprichada",
        _ => quality,
    };

    /// <summary>"Lote enviado às 23:41 · 3.147 linhas · caprichada · R$ 13,46".</summary>
    private static string DescribeSent(NightBatchState state) =>
        $"Lote enviado às {state.SubmittedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)} · {Lines(state.LineCount, "linha", "linhas")} · {QualityName(state.Quality)} · {Money.Brl(state.EstimatedBrl)}";

    /// <summary>"1.951 de 3.147 linhas prontas · verificado às 23:55": a API conta pedidos, então as linhas prontas são a parte proporcional.</summary>
    private static string DescribeProgress(NightBatchState state, MessageBatchInfo? info)
    {
        var at = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (state.Phase == NightBatchPhase.Canceling)
        {
            return $"Cancelando: a API encerra o lote e devolve o que já estava pronto · verificado às {at}";
        }

        var ready = info is null ? 0 : (int)Math.Round(info.Fraction * state.LineCount);
        return $"{ProjectLabels.Number(ready)} de {Lines(state.LineCount, "linha pronta", "linhas prontas")} · verificado às {at}";
    }

    /// <summary>"Chegou às 05:12 · 1.204 linhas gravadas · 3 voltaram com erro".</summary>
    private static string DescribeArrived(NightBatchState state, NightBatchOutcome outcome)
    {
        var parts = new List<string>
        {
            $"Chegou às {(state.EndedAt ?? state.SubmittedAt).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}",
            Lines(outcome.Translated, "linha gravada", "linhas gravadas"),
        };
        var errors = outcome.Failed + outcome.Refused;
        if (errors > 0)
        {
            parts.Add(errors == 1 ? "1 voltou com erro" : $"{ProjectLabels.Number(errors)} voltaram com erro");
        }

        return string.Join(" · ", parts);
    }

    private static (string Text, ToastKind Kind) DescribeNightOutcome(NightBatchState state, NightBatchOutcome outcome)
    {
        var parts = new List<string> { $"O lote chegou: {Lines(outcome.Translated, "linha traduzida pela IA", "linhas traduzidas pela IA")}." };
        var errors = outcome.Failed + outcome.Refused;
        if (errors > 0)
        {
            parts.Add($"{Lines(errors, "linha voltou", "linhas voltaram")} com erro e segue sem tradução.");
        }

        if (outcome.Skipped > 0)
        {
            parts.Add($"{Lines(outcome.Skipped, "linha mudou", "linhas mudaram")} durante a espera e {(outcome.Skipped == 1 ? "ficou" : "ficaram")} como você deixou.");
        }

        if (errors == 0 && outcome.Translated > 0)
        {
            parts.Add("Elas aparecem como IA no Editor, para você revisar.");
        }

        return (string.Join(' ', parts), errors > 0 || outcome.Translated == 0 ? ToastKind.Warning : ToastKind.Success);
    }
}
