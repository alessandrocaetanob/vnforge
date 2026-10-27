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
    public async Task Repetir_uma_etapa_concluida_abre_outro_teste()
    {
        await Done("Build de Jogo", Test);
        await Done("Injetar no jogo", Test);
        await Done("Build de Jogo", Test);

        Assert.Equal(2, bar.Entries.Count);
        Assert.Equal(["Build de Jogo"], bar.Entries[0].Steps.Select(s => s.Title));
        Assert.Equal(["Build de Jogo", "Injetar no jogo"], bar.Entries[1].Steps.Select(s => s.Title));
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

        Assert.Equal(BackgroundTaskState.Failed, Assert.Single(bar.Entries).State);
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
    public async Task Depois_de_duas_horas_parado_o_proximo_passo_abre_outro_teste()
    {
        await Done("Build de Jogo", Test);
        clock.Advance(BackgroundTaskService.GroupIdleLimit + TimeSpan.FromMinutes(1));
        await Done("Injetar no jogo", Test);

        Assert.Equal(2, bar.Entries.Count);
    }

    [Fact]
    public async Task Antes_das_duas_horas_o_proximo_passo_continua_o_mesmo_teste()
    {
        await Done("Build de Jogo", Test);
        clock.Advance(BackgroundTaskService.GroupIdleLimit - TimeSpan.FromMinutes(1));
        await Done("Injetar no jogo", Test);

        Assert.Single(bar.Entries);
    }

    [Fact]
    public async Task Testes_de_projetos_diferentes_nao_se_misturam()
    {
        await Done("Build de A", new TaskGroupSpec("teste-no-jogo:C:/a", "Teste no jogo"));
        await Done("Injetar no jogo", new TaskGroupSpec("teste-no-jogo:C:/b", "Teste no jogo"));

        Assert.Equal(2, bar.Entries.Count);
    }

    [Fact]
    public async Task Tarefa_avulsa_continua_uma_linha_propria()
    {
        await Done("Extrair Jogo");

        var entry = Assert.Single(bar.Entries);
        Assert.False(entry.IsGroup);
        Assert.Equal("Extrair Jogo", entry.Title);
        Assert.Equal("14:32", entry.Subtitle);
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
    public async Task A_tarefa_em_andamento_fica_no_topo_e_as_terminadas_vem_da_mais_recente_para_a_mais_antiga()
    {
        await Done("Primeira");
        clock.Advance(TimeSpan.FromMinutes(5));
        await Done("Segunda");
        clock.Advance(TimeSpan.FromMinutes(5));
        var gate = new TaskCompletionSource();
        var running = service.Start("Rodando", (_, _) => gate.Task);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Done("Terceira");

        Assert.Equal(["Rodando", "Terceira", "Segunda", "Primeira"], bar.Entries.Select(e => e.Title));

        // Ao terminar, a tarefa entra pela hora em que terminou, não pela de início: a que acabou por último fica em cima.
        clock.Advance(TimeSpan.FromMinutes(5));
        gate.SetResult();
        await running.Completion;
        Assert.Equal(["Rodando", "Terceira", "Segunda", "Primeira"], bar.Entries.Select(e => e.Title));
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
    public async Task A_linha_acompanha_a_mensagem_e_o_custo_da_tarefa()
    {
        var gate = new TaskCompletionSource();
        var reported = new TaskCompletionSource<IBackgroundTaskReporter>();
        var item = service.Start("Traduzindo o jogo", (reporter, _) =>
        {
            reporter.Report(0.5, "Cena 2 de 4");
            reported.SetResult(reporter);
            return gate.Task;
        }, usesAi: true);
        var worker = await reported.Task;
        Assert.Equal("14:32 · Cena 2 de 4", bar.Entries[0].Subtitle);
        var seen = new List<string?>();
        bar.Entries[0].PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        worker.Report(0.75, "Cena 3 de 4");
        worker.AddCost(2);

        Assert.Equal("14:32 · Cena 3 de 4", bar.Entries[0].Subtitle);
        Assert.Contains(string.Empty, seen);
        gate.SetResult();
        await item.Completion;
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
    public async Task Depois_de_limpar_o_proximo_passo_abre_um_teste_novo()
    {
        await Done("Build de Jogo", Test);
        bar.ClearFinishedCommand.Execute(null);

        await Done("Injetar no jogo", Test);

        var entry = Assert.Single(bar.Entries);
        Assert.Equal(["Injetar no jogo"], entry.Steps.Select(s => s.Title));
    }
}
