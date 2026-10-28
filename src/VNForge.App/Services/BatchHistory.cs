using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>O "Desfazer" de uma tarefa em lote terminou: o projeto cujas linhas voltaram e o que voltou.</summary>
public sealed record BatchUndone(string ProjectRoot, RestoreUndoResult Result);

/// <summary>
/// Liga as operações em lote ao ponto de restauração do Core e à lista de tarefas: <see cref="Run{T}(string, string, Func{ProjectFolder, T}, IEnumerable{string}?)"/> guarda o estado
/// das linhas antes da operação e põe na lista uma tarefa com "Desfazer"; <see cref="AttachAsync"/> faz o mesmo para a tarefa que já está na lista (a tradução da IA);
/// <see cref="LoadPersisted"/> traz de volta os pontos de sessões anteriores. A #479 (pedido em lote) usa <see cref="Run{T}(string, string, Func{ProjectFolder, T}, IEnumerable{string}?)"/>.
/// </summary>
public sealed class BatchHistory(BackgroundTaskService tasks, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    /// <summary>Depois que um "Desfazer" gravou, na thread da interface: quem mostra as linhas relê o projeto.</summary>
    public event EventHandler<BatchUndone>? Undone;

    /// <summary>
    /// Roda <paramref name="operation"/> guardando o estado dos scripts antes dela. Se ela mudou linhas, a lista de tarefas ganha uma tarefa concluída com
    /// "Desfazer". Pode ser chamado de qualquer thread. Se o ponto não puder ser guardado, a operação roda do mesmo jeito.
    /// </summary>
    /// <param name="scriptKeys">Os scripts que a operação pode mudar; nulo guarda todos.</param>
    public T Run<T>(string projectRoot, string title, Func<ProjectFolder, T> operation, IEnumerable<string>? scriptKeys = null)
    {
        var folder = ProjectFolder.Open(projectRoot);
        var store = new RestorePointStore(folder, clock);
        RestorePointRecorder? recorder = null;
        try
        {
            recorder = store.Begin(title, scriptKeys);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return operation(folder);
        }

        T result;
        try
        {
            result = operation(folder);
        }
        catch
        {
            TryRegister(projectRoot, recorder, title);
            throw;
        }

        TryRegister(projectRoot, recorder, title);
        return result;
    }

    /// <summary>Como <see cref="Run{T}(string, string, Func{ProjectFolder, T}, IEnumerable{string}?)"/>, quando <paramref name="history"/> existe; sem ele, só a operação.</summary>
    public static T Run<T>(BatchHistory? history, string projectRoot, string title, Func<ProjectFolder, T> operation, IEnumerable<string>? scriptKeys = null) =>
        history is null ? operation(ProjectFolder.Open(projectRoot)) : history.Run(projectRoot, title, operation, scriptKeys);

    /// <summary>Guarda o estado dos scripts antes de uma operação longa que já tem tarefa na lista; <see cref="AttachAsync"/> liga o resultado a ela.</summary>
    public async Task<RestorePointRecorder?> BeginAsync(string projectRoot, string title, IEnumerable<string>? scriptKeys = null, Func<TextUnit, TextUnit, bool>? include = null)
    {
        try
        {
            return await Task.Run(() => new RestorePointStore(ProjectFolder.Open(projectRoot), clock).Begin(title, scriptKeys, include)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Fecha a captura de <see cref="Begin"/> e dá ao <paramref name="task"/> o ponto de restauração, se a operação mudou linhas. Chame da thread da interface.</summary>
    public async Task AttachAsync(BackgroundTaskItem task, string projectRoot, RestorePointRecorder? recorder)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (recorder is null)
        {
            return;
        }

        try
        {
            if (await Task.Run(recorder.Complete).ConfigureAwait(true) is { } point)
            {
                task.Restore = new BatchRestore(projectRoot, point.Id, point.LineCount);
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // Sem o ponto não há "Desfazer" para esta tarefa; as linhas já estão gravadas.
        }
    }

    /// <summary>Põe na lista os pontos guardados do projeto que ainda não estão nela, para o "Desfazer" valer dias depois. Chame da thread da interface.</summary>
    public void LoadPersisted(string projectRoot)
    {
        IReadOnlyList<RestorePoint> points;
        try
        {
            points = new RestorePointStore(ProjectFolder.Open(projectRoot), clock).List();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var point in points.OrderBy(p => p.CreatedAt))
        {
            tasks.AddFinished(point.Title, point.CreatedAt.ToLocalTime(), Lines(point.LineCount), new BatchRestore(projectRoot, point.Id, point.LineCount));
        }
    }

    /// <summary>Desfaz o lote da tarefa. O resultado fica em <see cref="BackgroundTaskItem.UndoMessage"/>. Chame da thread da interface.</summary>
    public async Task UndoAsync(BackgroundTaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Restore is not { } restore || task.IsUndone || task.IsUndoing)
        {
            return;
        }

        task.IsUndoing = true;
        try
        {
            var result = await Task.Run(() => new RestorePointStore(ProjectFolder.Open(restore.ProjectRoot), clock).Undo(restore.PointId)).ConfigureAwait(true);
            task.IsUndone = true;
            task.UndoMessage = Describe(result);
            Undone?.Invoke(this, new BatchUndone(restore.ProjectRoot, result));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // O ponto que já não existe (desfeito em outra janela, fora do prazo) não tem como ser desfeito de novo.
            if (e is VNForgeException && new RestorePointStore(ProjectFolder.Open(restore.ProjectRoot), clock).Find(restore.PointId) is null)
            {
                task.IsUndone = true;
            }

            task.UndoMessage = $"Não foi possível desfazer: {e.Message}";
        }
        finally
        {
            task.IsUndoing = false;
        }
    }

    /// <summary>"12 linhas voltaram ao que eram. 3 foram editadas depois e ficaram como estão."</summary>
    public static string Describe(RestoreUndoResult result)
    {
        var parts = new List<string>();
        if (result.Restored > 0)
        {
            parts.Add(result.Restored == 1 ? "1 linha voltou ao que era." : $"{ProjectLabels.Number(result.Restored)} linhas voltaram ao que eram.");
        }
        else
        {
            parts.Add("Nenhuma linha voltou.");
        }

        if (result.EditedSince > 0)
        {
            parts.Add(result.EditedSince == 1
                ? "1 foi editada depois e ficou como está."
                : $"{ProjectLabels.Number(result.EditedSince)} foram editadas depois e ficaram como estão.");
        }

        if (result.Missing > 0)
        {
            parts.Add(result.Missing == 1
                ? "1 já não existe no script."
                : $"{ProjectLabels.Number(result.Missing)} já não existem no script.");
        }

        return string.Join(' ', parts);
    }

    private static string Lines(int count) => count == 1 ? "1 linha" : $"{ProjectLabels.Number(count)} linhas";

    private void TryRegister(string projectRoot, RestorePointRecorder recorder, string title)
    {
        try
        {
            var point = recorder.Complete();
            if (point is not null)
            {
                tasks.AddFinished(title, clock.GetLocalNow(), Lines(point.LineCount), new BatchRestore(projectRoot, point.Id, point.LineCount));
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // O ponto é um extra: a operação já gravou, e um disco ocupado agora só tira o "Desfazer" dela.
        }
    }
}
