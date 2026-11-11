using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;

namespace VNForge.App.Tests;

/// <summary>A lista de tarefas: as etapas de um teste no jogo numa linha só, a tarefa em andamento no topo e o custo só na IA.</summary>
public class TaskEntriesTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 14, 32, 0, TimeSpan.Zero);
    private static readonly TaskGroupSpec Test = new("teste-no-jogo:C:/projeto", "Teste no jogo");

    private readonly FakeTimeProvider clock = new(Start);
    private readonly BackgroundTaskService service;
    private readonly TaskBarViewModel bar;

    public TaskEntriesTests()
    {
        service = new BackgroundTaskService(time: clock);
        bar = new TaskBarViewModel(service, clock);
    }

    private Task Done(string title, TaskGroupSpec? group = null, bool usesAi = false) =>
        service.Start(title, (_, _) => Task.CompletedTask, group, usesAi).Completion;

    private BackgroundTaskItem Fails(string title, TaskGroupSpec? group = null) =>
        service.Start(title, (_, _) => throw new InvalidOperationException("sem espaço"), group);

    [Fact]
    public async Task As_etapas_do_mesmo_teste_viram_uma_linha_com_hora_e_resultado()
    {
        await Done("Restaurar originais", Test);
        await Done("Aplicar patch no executável", Test);
        await Done("Build de Jogo", Test);
        await Done("Injetar no jogo", Test);

        var entry = Assert.Single(bar.Entries);
        Assert.True(entry.IsGroup);
        Assert.Equal("Teste no jogo", entry.Title);
        Assert.Equal("14:32", entry.TimeText);
        Assert.Equal("Concluída", entry.StateText);
        Assert.Equal("14:32 · 4 etapas", entry.Subtitle);
        Assert.Equal(["Restaurar originais", "Aplicar patch no executável", "Build de Jogo", "Injetar no jogo"], entry.Steps.Select(s => s.Title));
        Assert.Equal("Tarefas (1)", bar.TasksLabel);
    }

    [Fact]
    public async Task Refazer_uma_etapa_que_falhou_fica_no_mesmo_teste_e_o_resultado_vale_a_ultima_tentativa()
    {
        var first = Fails("Build de Jogo", Test);
        await first.Completion;
        var entry = Assert.Single(bar.Entries);
        Assert.Equal(BackgroundTaskState.Failed, entry.State);
        Assert.Equal("Falhou", entry.StateText);

        await Done("Build de Jogo", Test);

        entry = Assert.Single(bar.Entries);
        Assert.Equal(2, entry.Steps.Count);
        Assert.Equal(BackgroundTaskState.Failed, entry.Steps[0].State);
        Assert.Equal(BackgroundTaskState.Completed, entry.State);
    }

    [Fact]
    public async Task Uma_etapa_que_falhou_e_nao_foi_refeita_deixa_o_teste_como_falhou()
    {
        await Done("Build de Jogo", Test);
        await Fails("Injetar no jogo", Test).Completion;
        await Done("Aplicar patch no executável", Test);

        var entry = Assert.Single(bar.Entries);
        Assert.Equal(BackgroundTaskState.Failed, entry.State);
        Assert.True(entry.IsFailed);
    }

    [Fact]
    public async Task Etapa_cancelada_deixa_o_teste_como_cancelado()
    {
        var started = new TaskCompletionSource();
        var item = service.Start("Build de Jogo", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, Test);
        await started.Task;
        item.CancelCommand.Execute(null);
        await item.Completion;

        var entry = Assert.Single(bar.Entries);
        Assert.Equal(BackgroundTaskState.Cancelled, entry.State);
        Assert.True(entry.IsCancelled);
        Assert.Equal("Cancelada", entry.StateText);
    }

    [Fact]
    public async Task A_hora_de_outro_dia_traz_o_dia_e_o_mes()
    {
        await Done("Extrair Jogo");
        clock.Advance(TimeSpan.FromDays(1));

        await Done("Outra");

        Assert.Equal("26/10 14:32", bar.Entries.Single(e => e.Title == "Extrair Jogo").TimeText);
        Assert.Equal("14:32", bar.Entries.Single(e => e.Title == "Outra").TimeText);
    }

    [Fact]
    public async Task Um_teste_em_andamento_sobe_para_o_topo_e_mostra_a_etapa_que_roda()
    {
        await Done("Extrair Jogo");
        var gate = new TaskCompletionSource();
        var step = service.Start("Build de Jogo", (_, _) => gate.Task, Test);

        Assert.Equal(["Teste no jogo", "Extrair Jogo"], bar.Entries.Select(e => e.Title));
        Assert.True(bar.Entries[0].IsRunning);
        Assert.Equal("14:32 · Build de Jogo", bar.Entries[0].Subtitle);

        // Uma etapa avulsa em andamento não é lote: não tem detalhe, barra nem ação de lote, e o desfazer é o comum.
        var running = bar.Entries[0];
        Assert.False(running.HasDetail);
        Assert.Equal(string.Empty, running.Detail);
        Assert.False(running.ShowsProgress);
        Assert.False(running.CanCheckNow);
        Assert.False(running.CanCancelBatch);
        Assert.False(running.HasBatchActions);
        Assert.Equal("Desfazer", running.UndoLabel);
        Assert.Equal(0, running.Percent);
        Assert.NotNull(running.CancelBatchCommand);

        gate.SetResult();
        await step.Completion;
        Assert.Equal("Concluída", bar.Entries[0].StateText);
    }

    [Fact]
    public async Task O_custo_aparece_so_na_tarefa_que_usa_a_IA()
    {
        service.Start("Traduzindo o jogo", (reporter, _) =>
        {
            reporter.AddCost(1);
            return Task.CompletedTask;
        }, usesAi: true);
        await service.Tasks[0].Completion;
        await Done("Extrair Jogo");
        await Done("Build de Jogo", Test);

        var ai = bar.Entries.Single(e => e.Title == "Traduzindo o jogo");
        Assert.True(ai.ShowsCost);
        Assert.Equal("≈ R$ 5,40 (cotação de exemplo)", ai.CostText);
        Assert.False(bar.Entries.Single(e => e.Title == "Extrair Jogo").ShowsCost);
        Assert.False(bar.Entries.Single(e => e.IsGroup).ShowsCost);
        Assert.True(service.Tasks[0].UsesAi);
        Assert.False(service.Tasks[1].UsesAi);
    }

    [Fact]
    public async Task Expandir_o_teste_sobrevive_a_reordenacao_da_lista()
    {
        await Done("Build de Jogo", Test);
        bar.Entries[0].ToggleExpandedCommand.Execute(null);
        Assert.True(bar.Entries[0].IsExpanded);

        clock.Advance(TimeSpan.FromMinutes(1));
        await Done("Extrair Jogo");

        Assert.Equal(["Extrair Jogo", "Teste no jogo"], bar.Entries.Select(e => e.Title));
        Assert.True(bar.Entries[1].IsExpanded);
    }

    [Fact]
    public async Task Limpar_concluidas_tira_as_linhas_terminadas_e_desliga_o_botao()
    {
        await Done("Build de Jogo", Test);
        await Done("Extrair Jogo");
        Assert.True(bar.HasFinished);

        bar.ClearFinishedCommand.Execute(null);

        Assert.Empty(bar.Entries);
        Assert.False(bar.HasFinished);
        Assert.Equal("Tarefas (0)", bar.TasksLabel);
    }

    [Fact]
    public async Task O_custo_fica_ao_lado_do_titulo_so_na_tarefa_de_IA_que_nao_e_lote()
    {
        await Done("Traduzir a cena", usesAi: true);
        await Done("Extrair Jogo");

        Assert.True(bar.Entries.Single(e => e.Title == "Traduzir a cena").ShowsCostBeside);
        Assert.False(bar.Entries.Single(e => e.Title == "Extrair Jogo").ShowsCostBeside);
        Assert.False(bar.ShowsNightBatch);
        Assert.False(bar.HasBatch);
        Assert.Equal("Tarefas (2)", bar.TasksLabel);
    }

    [Fact]
    public async Task Um_lote_em_andamento_mostra_o_detalhe_a_barra_e_as_acoes_do_lote_e_depois_a_mensagem()
    {
        var gate = new TaskCompletionSource();
        var batch = service.Start("Lote noturno", (_, _) => gate.Task);
        batch.IsBatch = true;
        batch.Detail = "12 de 40 linhas";
        batch.Progress = 0.3;
        batch.CheckNowCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        var entry = Assert.Single(bar.Entries);

        Assert.True(entry.IsBatch);
        Assert.True(entry.ShowsProgress);
        Assert.Equal("12 de 40 linhas", entry.Detail);
        Assert.True(entry.HasDetail);
        Assert.Equal(30, entry.Percent, 3);
        Assert.Same(batch.CheckNowCommand, entry.CheckNowCommand);
        Assert.True(entry.CanCheckNow);
        Assert.True(entry.CanCancelBatch);
        Assert.True(entry.HasBatchActions);
        Assert.Equal("Desfazer lote", entry.UndoLabel);
        Assert.Same(batch.CancelCommand, entry.CancelBatchCommand);

        batch.Message = "Lote pronto: 40 linhas.";
        Assert.Equal("Lote pronto: 40 linhas.", entry.Subtitle);
        gate.SetResult();
        await batch.Completion;

        Assert.False(entry.CanCheckNow);
        Assert.False(entry.ShowsProgress);
    }
}
