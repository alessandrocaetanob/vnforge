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
            Toasts.Show($"O lote não foi enviado: {DescribeNightError(e)}", ToastKind.Error, key: ToastKey);
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

    /// <summary>Quantas verificações seguidas podem falhar antes de a tarefa desistir e mostrar o erro; o lote segue na API e o app o retoma ao abrir de novo.</summary>
    public const int MaxCheckFailures = 5;

    private async Task WatchAsync(string projectRoot, NightBatchState initial, Func<IMessageBatchClient> factory)
    {
        using var wake = new SemaphoreSlim(0, int.MaxValue);
        NightBatchCheck? last = null;
        var canceled = false;
        string? cancelError = null;
        var alreadyEnded = false;
        BackgroundTaskItem task;
        try
        {
            var service = new NightBatchService(factory(), ProjectFolder.Open(projectRoot));
            failedBatchTasks.Remove(initial.Id);
            task = TaskService.Start(NightPrefix + initial.Title, async (reporter, ct) =>
            {
                reporter.Report(0);
                var failures = 0;
                try
                {
                    while (true)
                    {
                        try
                        {
                            last = await service.CheckAsync(initial.Id, ct).ConfigureAwait(false);
                            failures = 0;
                        }
                        catch (Exception e) when (e is not NightBatchException and not OperationCanceledException)
                        {
                            // Rede fora do ar numa verificação: o lote continua na API, então a tarefa segue esperando. Chave recusada ou lote que
                            // não existe (401, 403, 404) não passa sozinho, e várias falhas seguidas também não: aí a tarefa mostra o erro.
                            var why = ClaudeErrorMessages.Describe(e);
                            if (ClaudeErrorMessages.IsPermanent(e) || ++failures >= MaxCheckFailures)
                            {
                                throw new NightBatchException($"Não foi possível verificar o lote {initial.Id}: {why} Ele segue na API; o app o retoma quando abrir de novo.", e);
                            }

                            var message = $"Não foi possível verificar agora: {why} Tentando de novo.";
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
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // "Cancelar lote" em qualquer ponto do laço, na verificação ou na espera, cancela o lote na API: sem isso ele seguiria rodando e sendo cobrado.
                    canceled = true;
                    (alreadyEnded, cancelError) = await TryCancelBatchAsync(service, initial.Id).ConfigureAwait(false);
                    throw;
                }
            }, usesAi: true);
        }
        catch (OperationCanceledException)
        {
            watchedBatches.Remove(initial.Id);
            return;
        }
        catch (Exception e)
        {
            // Sem a chave, sem a pasta ou com o disco fora do ar: a lista mostra o lote com o motivo, em vez de a tarefa falhar calada.
            watchedBatches.Remove(initial.Id);
            var text = DescribeNightError(e, initial.Id);
            // SetProject roda mais de uma vez por abertura e cada passada tenta retomar o lote de novo: a linha de falha que já está na lista basta.
            if (!failedBatchTasks.TryGetValue(initial.Id, out var previous) || !TaskService.Tasks.Contains(previous))
            {
                var failed = TaskService.Start(NightPrefix + initial.Title, (_, _) => Task.FromException(new NightBatchException(text, e)), usesAi: true);
                failed.IsBatch = true;
                failedBatchTasks[initial.Id] = failed;
            }

            Toasts.Show(text, ToastKind.Error, key: ToastKey);
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
            const string EndedText = "O lote já tinha terminado na API, então não há o que cancelar. O resultado é gravado em seguida.";
            TaskService.Post(() => Finish(task, cancelError is not null ? $"Não foi possível cancelar na API: {cancelError}" : alreadyEnded ? EndedText : CancelText));
            if (cancelError is not null)
            {
                Toasts.Show($"Não foi possível cancelar o lote na API: {cancelError} Ele segue rodando; tente de novo.", ToastKind.Error, key: ToastKey);
            }
            else
            {
                Toasts.Show(alreadyEnded ? EndedText : CancelText, alreadyEnded ? ToastKind.Info : ToastKind.Warning, key: ToastKey);
            }

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
                task.ShowLinesCommand = new RelayCommand(() => ShowUnfinishedLine(projectRoot, unfinished[0]));
            }
        });

        var (text2, kind) = DescribeNightOutcome(arrived, outcome);
        Toasts.Show(text2, kind, key: ToastKey);

        // O lote pode chegar com outro projeto aberto: o Editor e as páginas de resumo são do projeto atual, e recarregá-los não mostraria o que o lote gravou.
        if (SameProject(projectRoot))
        {
            Editor.Reload();
            await RefreshSummaryPageAsync();
        }
    }

    private bool SameProject(string projectRoot) =>
        CurrentProject is { } current && string.Equals(Path.GetFullPath(current.Root), Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase);

    /// <summary>"Ver as N linhas": abre a primeira linha sem tradução no Editor, se o projeto do lote é o aberto; senão avisa qual projeto abrir.</summary>
    private void ShowUnfinishedLine(string projectRoot, string unitId)
    {
        if (SameProject(projectRoot))
        {
            OpenLineInEditor(LineOf(unitId));
            return;
        }

        Toasts.Show($"Essas linhas são de outro projeto. Abra {Path.GetFileName(Path.TrimEndingDirectorySeparator(projectRoot))} para vê-las.", ToastKind.Info, key: ToastKey);
    }

    /// <summary>A frase de um erro ao enviar ou acompanhar o lote. A fábrica do cliente lança <see cref="InvalidOperationException"/> quando falta a chave.</summary>
    private static string DescribeNightError(Exception e, string? batchId = null)
    {
        var tail = batchId is null ? string.Empty : $" O lote {batchId} segue na API e o app o retoma quando a chave voltar.";
        return e switch
        {
            InvalidOperationException => $"Sem chave de API: informe a chave em Ajustes, na aba IA e gasto.{tail}",
            VNForgeException or IOException or UnauthorizedAccessException => $"{e.Message}{(batchId is null ? string.Empty : $" O lote {batchId} segue na API.")}",
            _ => $"{ClaudeErrorMessages.Describe(e)}{(batchId is null ? string.Empty : $" O lote {batchId} segue na API.")}",
        };
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
    private readonly Dictionary<string, BackgroundTaskItem> failedBatchTasks = [];

    private void SetDetail(string batchId, string detail)
    {
        if (batchTasks.TryGetValue(batchId, out var task) && task.IsRunning)
        {
            task.Detail = detail;
        }
    }

    private async Task WaitAsync(SemaphoreSlim wake, CancellationToken ct) => await wake.WaitAsync(NightBatchPollInterval, ct).ConfigureAwait(false);

    /// <summary>Pede à API o cancelamento do lote. Devolve se ela já o tinha terminado (nada a cancelar) e o motivo em português quando não aceitou o pedido.</summary>
    private static async Task<(bool AlreadyEnded, string? Error)> TryCancelBatchAsync(NightBatchService service, string batchId)
    {
        try
        {
            var state = await service.CancelAsync(batchId, CancellationToken.None).ConfigureAwait(false);
            return (state.Phase != NightBatchPhase.Canceling, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (false, e is VNForgeException or IOException or UnauthorizedAccessException ? e.Message : ClaudeErrorMessages.Describe(e));
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
